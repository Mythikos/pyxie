namespace Pyxie.Data.Entities;

/// <summary>A card editor's upload session, bound to the card and version it opened on. New cards have neither.</summary>
public sealed class MediaUpload
{
	/// <summary>Gets or sets the unguessable session identifier.</summary>
	public Guid Id { get; set; }
	/// <summary>Gets or sets the card being edited; null for a new card.</summary>
	public int? CardId { get; set; }
	/// <summary>Gets or sets the version the editor opened on.</summary>
	public int? CardVersion { get; set; }
	/// <summary>Gets or sets the session's expiration in UTC.</summary>
	public DateTime ExpiresUtc { get; set; }
}
