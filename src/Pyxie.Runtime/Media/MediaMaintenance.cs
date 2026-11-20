using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;

namespace Pyxie.Runtime.Media;

/// <summary>Reclaims abandoned uploads and unreferenced objects without racing publication or project removal.</summary>
public sealed partial class MediaMaintenance : BackgroundService
{
	private readonly OpenProject _openProject;
	private readonly IProjectDbContextSource _contexts;
	private readonly MediaStorage _storage;
	private readonly TimeProvider _clock;
	private readonly ILogger<MediaMaintenance> _logger;

	/// <summary>Initializes media maintenance.</summary>
	/// <param name="openProject">The current project.</param>
	/// <param name="contexts">Creates project-pinned contexts.</param>
	/// <param name="storage">Serializes object writes.</param>
	/// <param name="clock">The expiration clock.</param>
	/// <param name="logger">Reports cleanup failures for a later retry.</param>
	public MediaMaintenance(OpenProject openProject, IProjectDbContextSource contexts, MediaStorage storage, TimeProvider clock, ILogger<MediaMaintenance> logger)
	{
		_openProject = openProject;
		_contexts = contexts;
		_storage = storage;
		_clock = clock;
		_logger = logger;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new(TimeSpan.FromMinutes(5), _clock);
		do
		{
			try
			{
				if (_openProject.Current is Project project)
				{
					await SweepAsync(project, stoppingToken);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or DbUpdateException)
			{
				LogCleanupFailed(_logger, exception);
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}

	/// <summary>Reclaims one project's expired metadata and unreferenced files.</summary>
	/// <param name="project">The captured project.</param>
	/// <param name="cancellationToken">A token to cancel the sweep.</param>
	/// <returns>A task completing after cleanup.</returns>
	public async Task SweepAsync(Project project, CancellationToken cancellationToken)
	{
		using IDisposable held = await _storage.AcquireAsync(cancellationToken);
		if (_openProject.Current?.Id != project.Id || !Directory.Exists(project.Folder))
		{
			return;
		}
		await using ProjectDbContext context = _contexts.CreateFor(project);
		await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);
		DateTime now = _clock.GetUtcNow().UtcDateTime;
		await context.MediaUploads.Where(upload => upload.ExpiresUtc <= now).ExecuteDeleteAsync(cancellationToken);
		await context.MediaFiles.Where(file => file.RemovedUtc != null && !context.RoundMediaInputs.Any(input => input.MediaFileId == file.Id)).ExecuteDeleteAsync(cancellationToken);
		HashSet<Guid> referenced = (await context.MediaFiles.Select(file => file.Id).ToListAsync(cancellationToken)).ToHashSet();
		string staging = Path.Combine(MediaPaths.Root(project.Folder), "staging");
		if (Directory.Exists(staging))
		{
			foreach (string path in Directory.EnumerateFiles(staging, "round-*.json"))
			{
				string number = Path.GetFileNameWithoutExtension(path)["round-".Length..];
				if (!int.TryParse(number, out int roundId))
				{
					continue;
				}
				CardRound? round = await context.Rounds.AsNoTracking().FirstOrDefaultAsync(item => item.Id == roundId, cancellationToken);
				if (round is null || round.Outcome is not null)
				{
					File.Delete(path);
				}
				else
				{
					MediaCollection receipt = await RoundMedia.ReadReceiptAsync(project.Folder, roundId, cancellationToken);
					referenced.UnionWith(receipt.Files.Select(file => file.Id));
				}
			}
			foreach (string path in Directory.EnumerateFiles(staging, "*.part"))
			{
				if (!_storage.IsReceiving(path))
				{
					File.Delete(path);
				}
			}
		}
		string objects = Path.Combine(MediaPaths.Root(project.Folder), "objects");
		if (Directory.Exists(objects))
		{
			foreach (string path in Directory.EnumerateFiles(objects))
			{
				if (Guid.TryParseExact(Path.GetFileName(path), "N", out Guid id) && !referenced.Contains(id))
				{
					File.Delete(path);
				}
			}
		}
		await transaction.CommitAsync(cancellationToken);
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "Media cleanup could not finish; it will be retried.")]
	private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
