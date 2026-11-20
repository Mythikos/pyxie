using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Reads card media and opens project-bound editor sessions. Card saves and round finalization own attachment writes.</summary>
public interface ICardMediaService
{
	/// <summary>Lists a card's current and historical media.</summary>
	/// <param name="projectId">The project shown by the caller.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="cancellationToken">A token to cancel reading.</param>
	/// <param name="count">Maximum number of files to read, or null for the complete list.</param>
	/// <param name="afterFileId">The last file already read, or null to start with the newest.</param>
	/// <returns>The files or a refusal.</returns>
	Task<ServiceResult<IReadOnlyList<MediaFile>>> ListAsync(int projectId, int cardId, CancellationToken cancellationToken, int? count = null, Guid? afterFileId = null);

	/// <summary>Looks up a single saved file on the requested card and project.</summary>
	/// <param name="projectId">The project shown by the caller.</param>
	/// <param name="cardId">The owning card.</param>
	/// <param name="fileId">The file identifier.</param>
	/// <param name="cancellationToken">Cancels the read.</param>
	/// <returns>The file or a refusal.</returns>
	Task<ServiceResult<MediaFile>> GetAsync(int projectId, int cardId, Guid fileId, CancellationToken cancellationToken);

	/// <summary>Opens an upload session only while the card remains editable at the shown version.</summary>
	/// <param name="projectId">The project shown by the editor.</param>
	/// <param name="cardId">The card, or null for a new one.</param>
	/// <param name="version">The shown version, or null for a new card.</param>
	/// <param name="cancellationToken">A token to cancel opening.</param>
	/// <returns>The session or a refusal.</returns>
	Task<ServiceResult<CardMediaSession>> BeginAsync(int projectId, int? cardId, int? version, CancellationToken cancellationToken);

	/// <summary>Discards an unsaved session. Already saved or expired sessions are harmless.</summary>
	/// <param name="projectId">The original project.</param>
	/// <param name="uploadId">The editor session.</param>
	/// <param name="cancellationToken">A token to cancel discarding.</param>
	/// <returns>A task that completes after discarding the session.</returns>
	Task AbandonAsync(int projectId, Guid uploadId, CancellationToken cancellationToken);
}
