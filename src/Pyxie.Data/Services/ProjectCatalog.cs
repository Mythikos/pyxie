using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="IProjectCatalog"/> backed by <see cref="MachineDbContext"/>.</summary>
public sealed class ProjectCatalog : IProjectCatalog
{
	private const string NotFoundMessage = "Project not found.";
	private const string NotOpenMessage = "The project is not open.";

	private readonly IDbContextFactory<MachineDbContext> _contextFactory;
	private readonly OpenProject _openProject;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="ProjectCatalog"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived machine database context per operation.</param>
	/// <param name="openProject">The open project, which this catalog sets after every committed open and close.</param>
	/// <param name="timeProvider">The clock that stamps the time a project was added.</param>
	public ProjectCatalog(IDbContextFactory<MachineDbContext> contextFactory, OpenProject openProject, TimeProvider timeProvider)
	{
		_contextFactory = contextFactory;
		_openProject = openProject;
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken)
	{
		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Projects
			.AsNoTracking()
			.OrderBy(project => project.Name)
			.ThenBy(project => project.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<Project?> GetOpenAsync(CancellationToken cancellationToken)
	{
		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Projects.AsNoTracking().FirstOrDefaultAsync(project => project.IsOpen, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<Project?> RestoreOpenAsync(CancellationToken cancellationToken)
	{
		Project? open = await GetOpenAsync(cancellationToken);
		_openProject.Set(open);

		return open;
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Project>> AddAsync(string name, string folder, string source, CancellationToken cancellationToken)
	{
		string normalizedName = InputRules.Normalize(name);
		string normalizedFolder = InputRules.Normalize(folder);
		string normalizedSource = InputRules.Normalize(source);
		string? inputError = InputRules.ValidateRequired(normalizedName, "Project name", PyxieLimits.MaxNameLength) ?? InputRules.ValidateRequired(normalizedFolder, "Folder", PyxieLimits.MaxPathLength) ?? (Path.IsPathFullyQualified(normalizedFolder) ? null : "Folder must be a full path.") ?? InputRules.ValidateRequired(normalizedSource, "Source", PyxieLimits.MaxOriginLength);
		if (inputError is not null)
		{
			return ServiceResult<Project>.Failure(inputError);
		}

		normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalizedFolder));

		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await DuplicateAsync(context, normalizedName, normalizedFolder, null, cancellationToken) is string duplicate)
		{
			return ServiceResult<Project>.Failure(duplicate);
		}

		// The check above cannot stop two callers that pass it together, and the unique indexes only stop exact duplicates. So the insert comes first, taking the database's write lock, and the case-insensitive check is made again after it, inside one transaction. Every return before the commit rolls back.
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Project project = new() { Name = normalizedName, Folder = normalizedFolder, Source = normalizedSource, CreatedUtc = _timeProvider.GetUtcNow().UtcDateTime };
		context.Projects.Add(project);

		try
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException)
		{
			// A caller with exactly the same name or folder won the race; any other failure is not ours to report.
			if (await DuplicateAsync(context, normalizedName, normalizedFolder, null, cancellationToken) is string raced)
			{
				return ServiceResult<Project>.Failure(raced);
			}

			throw;
		}

		if (await DuplicateAsync(context, normalizedName, normalizedFolder, project.Id, cancellationToken) is string concurrent)
		{
			return ServiceResult<Project>.Failure(concurrent);
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Project>.Success(project);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Project>> OpenAsync(int id, CancellationToken cancellationToken)
	{
		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		// The other projects are closed before the project is opened, because the unique index on open projects refuses two open ones even inside a transaction. Closing is the transaction's first statement, so it takes the database's write lock before anything is read. A project that does not exist ends the transaction without a commit, which rolls the close back, so a refusal changes nothing.
		await context.Projects.Where(other => other.IsOpen && other.Id != id).ExecuteUpdateAsync(changes => changes.SetProperty(other => other.IsOpen, false), cancellationToken);

		int opened = await context.Projects.Where(project => project.Id == id).ExecuteUpdateAsync(changes => changes.SetProperty(project => project.IsOpen, true), cancellationToken);
		if (opened == 0)
		{
			return ServiceResult<Project>.Failure(NotFoundMessage);
		}

		Project project = await context.Projects.AsNoTracking().SingleAsync(existing => existing.Id == id, cancellationToken);
		await transaction.CommitAsync(cancellationToken);
		_openProject.Set(project);

		return ServiceResult<Project>.Success(project);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken)
	{
		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		// The project is closed only while it is the open one, in the same statement that checks it, so a project opened since the caller looked is never closed in its place. The reason for a refusal is read afterwards: it changes nothing either way.
		int closed = await context.Projects.Where(project => project.Id == id && project.IsOpen).ExecuteUpdateAsync(changes => changes.SetProperty(project => project.IsOpen, false), cancellationToken);
		if (closed > 0)
		{
			_openProject.Set(null);

			return ServiceResult.Success();
		}

		bool exists = await context.Projects.AnyAsync(project => project.Id == id, cancellationToken);

		return ServiceResult.Failure(exists ? NotOpenMessage : NotFoundMessage);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> RemoveAsync(int id, CancellationToken cancellationToken)
	{
		await using MachineDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		int removed = await context.Projects.Where(project => project.Id == id && !project.IsOpen).ExecuteDeleteAsync(cancellationToken);
		if (removed > 0)
		{
			return ServiceResult.Success();
		}

		bool exists = await context.Projects.AnyAsync(project => project.Id == id, cancellationToken);

		return ServiceResult.Failure(exists ? "Close the project before removing it." : NotFoundMessage);
	}

	// Names and folders are unique ignoring case: a folder differing only in case is the same folder on Windows, and two names differing only in case would look like one project.
	private static async Task<string?> DuplicateAsync(MachineDbContext context, string name, string folder, int? excludedId, CancellationToken cancellationToken)
	{
		List<Project> others = await context.Projects.AsNoTracking().Where(project => project.Id != excludedId).ToListAsync(cancellationToken);

		if (InputRules.ContainsName(others.Select(project => project.Name), name))
		{
			return $"A project named '{name}' already exists.";
		}

		Project? sameFolder = others.FirstOrDefault(project => string.Equals(project.Folder, folder, StringComparison.OrdinalIgnoreCase));

		return sameFolder is null ? null : $"The folder {folder} already belongs to the project '{sameFolder.Name}'.";
	}
}
