using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;

namespace Pyxie.Data.Media;

/// <summary>Media changes made inside the transaction that saves a card or finishes its round.</summary>
public static class MediaStore
{
	/// <summary>Applies an editor's entire context selection. The caller owns the card write lock and rolls back on refusal.</summary>
	/// <param name="context">The transaction's context.</param>
	/// <param name="cardId">The saved card.</param>
	/// <param name="originalCardId">Null when creating a card.</param>
	/// <param name="originalVersion">The version before this save.</param>
	/// <param name="edit">The desired selection.</param>
	/// <param name="now">The current UTC time.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A refusal, or null once the selection is saved.</returns>
	public static async Task<string?> ApplyEditAsync(ProjectDbContext context, int cardId, int? originalCardId, int? originalVersion, MediaEdit? edit, DateTime now, CancellationToken cancellationToken)
	{
		if (edit is null)
		{
			return null;
		}

		MediaUpload? upload = await context.MediaUploads.FirstOrDefaultAsync(item => item.Id == edit.UploadId, cancellationToken);
		if (upload is null || upload.ExpiresUtc <= now || upload.CardId != originalCardId || upload.CardVersion != originalVersion)
		{
			return "This card's upload session changed or expired. Reopen the card before saving.";
		}

		Guid[] ids = [.. edit.FileIds.Distinct()];
		if (ids.Length != edit.FileIds.Count || ids.Length > MediaLimits.MaxFiles)
		{
			return $"A card can have at most {MediaLimits.MaxFiles} context files, each selected once.";
		}

		List<MediaFile> selected = await context.MediaFiles.Where(file => ids.Contains(file.Id)).ToListAsync(cancellationToken);
		if (selected.Count != ids.Length || selected.Any(file => file.RoundId is not null || file.RemovedUtc is not null || (file.UploadId != upload.Id && (file.CardId != originalCardId || originalCardId is null))))
		{
			return "One of the selected files no longer belongs to this card's context. Reopen the card before saving.";
		}

		if (selected.Sum(file => file.Length) > MediaLimits.MaxBatchBytes)
		{
			return "A card's context files cannot exceed 1 GiB.";
		}

		List<MediaFile> current = await context.MediaFiles.Where(file => file.CardId == cardId && file.RoundId == null && file.RemovedUtc == null).ToListAsync(cancellationToken);
		bool changed = !current.Select(file => file.Id).ToHashSet().SetEquals(ids);
		foreach (MediaFile file in current.Where(file => !ids.Contains(file.Id)))
		{
			file.RemovedUtc = now;
		}

		foreach (MediaFile file in selected)
		{
			file.UploadId = null;
			file.CardId = cardId;
		}

		if (changed)
		{
			await context.Cards.Where(card => card.Id == cardId).ExecuteUpdateAsync(changes => changes.SetProperty(card => card.ResumeRound, (RoundKind?)null), cancellationToken);
		}

		await context.SaveChangesAsync(cancellationToken);
		context.MediaUploads.Remove(upload);
		await context.SaveChangesAsync(cancellationToken);
		return null;
	}

	/// <summary>Chooses current user context and the latest output-producing round of each agent stage.</summary>
	/// <param name="card">The card with media and rounds loaded.</param>
	/// <returns>The immutable selection to hand to the next agent.</returns>
	public static IReadOnlyList<MediaFile> InputsFor(WorkCard card)
	{
		HashSet<int> latest = [.. card.Rounds.Where(round => round.Kind.RunsAgent() && card.MediaFiles.Any(file => file.RoundId == round.Id)).GroupBy(round => round.Kind).Select(group => group.MaxBy(round => round.Sequence)).OfType<CardRound>().Select(round => round.Id)];
		return [.. card.MediaFiles.Where(file => file.RemovedUtc is null && (file.RoundId is null || latest.Contains(file.RoundId.Value))).OrderBy(file => file.CreatedUtc).ThenBy(file => file.Id)];
	}

	/// <summary>Adds validated, durable output metadata while the producing round is finalized. Replaying the same publication does nothing.</summary>
	/// <param name="context">The finalization transaction's context.</param>
	/// <param name="card">The producing card.</param>
	/// <param name="roundId">The round whose output is being recorded.</param>
	/// <param name="files">Files copied into permanent storage by the runtime.</param>
	public static void AddOutputs(ProjectDbContext context, WorkCard card, int roundId, IReadOnlyList<MediaFile> files)
	{
		foreach (MediaFile file in files)
		{
			if (file.CardId != card.Id || file.RoundId != roundId || file.UploadId is not null || string.IsNullOrWhiteSpace(file.PublicationKey) || file.Length < 0 || file.Length > MediaLimits.MaxFileBytes)
			{
				throw new InvalidOperationException("The runtime supplied media for a different round or without a valid publication.");
			}

			if (card.MediaFiles.Any(existing => existing.Id == file.Id || (existing.RoundId == roundId && existing.PublicationKey == file.PublicationKey)))
			{
				continue;
			}

			card.MediaFiles.Add(file);
			context.MediaFiles.Add(file);
		}
	}
}
