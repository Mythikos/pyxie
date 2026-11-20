using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Project-pinned media metadata operations.</summary>
public sealed class CardMediaService : ICardMediaService
{
	private readonly OpenProject _openProject;
	private readonly IProjectDbContextSource _contexts;
	private readonly TimeProvider _clock;

	/// <summary>Initializes media metadata operations.</summary>
	/// <param name="openProject">The currently open project.</param>
	/// <param name="contexts">Creates contexts for the captured project.</param>
	/// <param name="clock">The upload expiration clock.</param>
	public CardMediaService(OpenProject openProject, IProjectDbContextSource contexts, TimeProvider clock)
	{
		_openProject = openProject;
		_contexts = contexts;
		_clock = clock;
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<IReadOnlyList<MediaFile>>> ListAsync(int projectId, int cardId, CancellationToken cancellationToken, int? count = null, Guid? afterFileId = null)
	{
		if (_openProject.Current is not Project project || project.Id != projectId)
		{
			return ServiceResult<IReadOnlyList<MediaFile>>.Failure("The project is no longer open.");
		}
		await using ProjectDbContext context = _contexts.CreateFor(project);
		if (!await context.Cards.AnyAsync(card => card.Id == cardId, cancellationToken))
		{
			return ServiceResult<IReadOnlyList<MediaFile>>.Failure("Card not found.");
		}
		IQueryable<MediaFile> files = context.MediaFiles.AsNoTracking().Where(file => file.CardId == cardId);
		if (afterFileId is Guid lastId)
		{
			DateTime? lastTime = await files.Where(file => file.Id == lastId).Select(file => (DateTime?)file.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
			if (lastTime is null)
			{
				return ServiceResult<IReadOnlyList<MediaFile>>.Failure("The last attachment is no longer on this card. Start the listing again.");
			}
			files = files.Where(file => file.CreatedUtc < lastTime || (file.CreatedUtc == lastTime && file.Id.CompareTo(lastId) > 0));
		}

		files = files.OrderByDescending(file => file.CreatedUtc).ThenBy(file => file.Id);
		if (count.HasValue)
		{
			files = files.Take(count.Value);
		}
		return ServiceResult<IReadOnlyList<MediaFile>>.Success(await files.ToListAsync(cancellationToken));
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<MediaFile>> GetAsync(int projectId, int cardId, Guid fileId, CancellationToken cancellationToken)
	{
		if (_openProject.Current is not Project project || project.Id != projectId)
		{
			return ServiceResult<MediaFile>.Failure("The project is no longer open.");
		}
		await using ProjectDbContext context = _contexts.CreateFor(project);
		MediaFile? file = await context.MediaFiles.AsNoTracking().FirstOrDefaultAsync(item => item.Id == fileId && item.CardId == cardId, cancellationToken);
		return file is null ? ServiceResult<MediaFile>.Failure("Attachment not found on this card.") : ServiceResult<MediaFile>.Success(file);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<CardMediaSession>> BeginAsync(int projectId, int? cardId, int? version, CancellationToken cancellationToken)
	{
		if (_openProject.Current is not Project project || project.Id != projectId)
		{
			return ServiceResult<CardMediaSession>.Failure("The project is no longer open.");
		}
		await using ProjectDbContext context = _contexts.CreateFor(project);
		if (cardId is int id && !await context.Cards.AnyAsync(card => card.Id == id && card.Lane == CardLane.Queue && card.Version == version, cancellationToken))
		{
			return ServiceResult<CardMediaSession>.Failure("The card changed. Reopen it before editing its media.");
		}
		if ((cardId is null) != (version is null))
		{
			return ServiceResult<CardMediaSession>.Failure("The card version is missing.");
		}
		MediaUpload upload = new() { Id = Guid.NewGuid(), CardId = cardId, CardVersion = version, ExpiresUtc = _clock.GetUtcNow().UtcDateTime + MediaLimits.UploadLifetime };
		context.MediaUploads.Add(upload);
		try
		{
			await context.SaveChangesAsync(cancellationToken);
		}
		catch (DbUpdateException) when (cardId is not null)
		{
			if (!await context.Cards.AnyAsync(card => card.Id == cardId, cancellationToken))
			{
				return ServiceResult<CardMediaSession>.Failure("Card not found.");
			}
			throw;
		}
		List<MediaFile> files = cardId is null ? [] : await context.MediaFiles.AsNoTracking().Where(file => file.CardId == cardId && file.RoundId == null && file.RemovedUtc == null).OrderBy(file => file.CreatedUtc).ToListAsync(cancellationToken);
		return ServiceResult<CardMediaSession>.Success(new CardMediaSession(upload.Id, files));
	}

	/// <inheritdoc/>
	public async Task AbandonAsync(int projectId, Guid uploadId, CancellationToken cancellationToken)
	{
		if (_openProject.Current is not Project project || project.Id != projectId)
		{
			return;
		}
		await using ProjectDbContext context = _contexts.CreateFor(project);
		await context.MediaUploads.Where(upload => upload.Id == uploadId).ExecuteDeleteAsync(cancellationToken);
	}
}
