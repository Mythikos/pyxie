using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Default <see cref="ICategoryService"/> backed by <see cref="ProjectDbContext"/>.</summary>
public sealed class CategoryService : ICategoryService
{
	private const string NotFoundMessage = "Category not found.";

	private readonly IDbContextFactory<ProjectDbContext> _contextFactory;

	/// <summary>Initializes a new instance of the <see cref="CategoryService"/> class.</summary>
	/// <param name="contextFactory">The factory used to create a short-lived context per operation.</param>
	public CategoryService(IDbContextFactory<ProjectDbContext> contextFactory)
	{
		_contextFactory = contextFactory;
	}

	/// <inheritdoc/>
	public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		return await context.Categories
			.AsNoTracking()
			.OrderBy(category => category.Name)
			.ThenBy(category => category.Id)
			.ToListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> CreateAsync(string name, HexColor color, CancellationToken cancellationToken)
	{
		string normalized = InputRules.Normalize(name);
		string? inputError = CategoryInputRules.ValidateName(normalized);
		if (inputError is not null)
		{
			return ServiceResult<Category>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);

		if (await IsNameTakenAsync(context, normalized, null, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		Category category = new() { Name = normalized, Color = color };
		context.Categories.Add(category);
		if (!await TrySaveAsync(context, normalized, null, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		return ServiceResult<Category>.Success(category);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Category>> RenameAsync(int id, string name, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, name ?? string.Empty, null, null, cancellationToken);

	/// <inheritdoc/>
	public Task<ServiceResult<Category>> SetColorAsync(int id, HexColor color, CancellationToken cancellationToken) => UpdateIfCurrentAsync(id, null, color, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> UpdateIfCurrentAsync(int id, string? name, HexColor? color, string? expectedRevision, CancellationToken cancellationToken)
	{
		string? normalized = name is null ? null : InputRules.Normalize(name);
		if (normalized is not null && CategoryInputRules.ValidateName(normalized) is string inputError)
		{
			return ServiceResult<Category>.Failure(inputError);
		}

		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		Category? category = await context.Categories.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (category is null)
		{
			return ServiceResult<Category>.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(category))
		{
			return ServiceResult<Category>.Failure(SettingsRevision.ChangedMessage);
		}

		if (normalized is not null && await IsNameTakenAsync(context, normalized, id, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(normalized));
		}

		category.Name = normalized ?? category.Name;
		category.Color = color ?? category.Color;
		if (!await TrySaveAsync(context, category.Name, id, cancellationToken))
		{
			return ServiceResult<Category>.Failure(DuplicateMessage(category.Name));
		}

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult<Category>.Success(category);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public async Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = await _contextFactory.CreateDbContextAsync(cancellationToken);
		await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

		Category? category = await context.Categories.FirstOrDefaultAsync(existing => existing.Id == id, cancellationToken);
		if (category is null)
		{
			return ServiceResult.Failure(NotFoundMessage);
		}

		if (expectedRevision is not null && expectedRevision != SettingsRevision.Of(category))
		{
			return ServiceResult.Failure(SettingsRevision.ChangedMessage);
		}

		int cardCount = await CategoryStore.CountCardsAsync(context, id, cancellationToken);
		if (cardCount > 0)
		{
			return ServiceResult.Failure(CategoryStore.InUseRefusal(category.Name, cardCount));
		}

		context.Categories.Remove(category);
		await context.SaveChangesAsync(cancellationToken);

		await transaction.CommitAsync(cancellationToken);

		return ServiceResult.Success();
	}

	private static string DuplicateMessage(string name)
	{
		return $"A category named '{name}' already exists.";
	}

	// A concurrent writer can win between the name check and the save; the unique index then rejects this write, which is reported as a duplicate.
	private static async Task<bool> TrySaveAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		try
		{
			await context.SaveChangesAsync(cancellationToken);

			return true;
		}
		catch (DbUpdateException)
		{
			if (await IsNameTakenAsync(context, name, excludedId, cancellationToken))
			{
				return false;
			}

			throw;
		}
	}

	private static async Task<bool> IsNameTakenAsync(ProjectDbContext context, string name, int? excludedId, CancellationToken cancellationToken)
	{
		List<string> otherNames = await context.Categories
			.AsNoTracking()
			.Where(category => category.Id != excludedId)
			.Select(category => category.Name)
			.ToListAsync(cancellationToken);

		return InputRules.ContainsName(otherNames, name);
	}
}
