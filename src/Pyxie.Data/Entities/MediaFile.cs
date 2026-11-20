namespace Pyxie.Data.Entities;

/// <summary>An immutable file staged for a card or retained with it. Bytes live in the project's media folder under this row's identifier.</summary>
public sealed class MediaFile
{
	/// <summary>Gets or sets the opaque storage identifier.</summary>
	public Guid Id { get; set; }
	/// <summary>Gets or sets the owning card, or null until an upload is saved.</summary>
	public int? CardId { get; set; }
	/// <summary>Gets or sets the upload session while the file is staged.</summary>
	public Guid? UploadId { get; set; }
	/// <summary>Gets or sets the producing round; null means user-supplied context.</summary>
	public int? RoundId { get; set; }
	/// <summary>Gets or sets the producer's relative path, unique within its round.</summary>
	public string? PublicationKey { get; set; }
	/// <summary>Gets or sets the display filename, never a storage path.</summary>
	public string Name { get; set; } = string.Empty;
	/// <summary>Gets or sets the content type established from the file's bytes.</summary>
	public string ContentType { get; set; } = "application/octet-stream";
	/// <summary>Gets or sets the actual byte count.</summary>
	public long Length { get; set; }
	/// <summary>Gets or sets the SHA-256 of the stored bytes.</summary>
	public string Sha256 { get; set; } = string.Empty;
	/// <summary>Gets or sets the optional plain-text caption.</summary>
	public string? Caption { get; set; }
	/// <summary>Gets or sets the commit observed by Pyxie for the producing round, when known.</summary>
	public string? Commit { get; set; }
	/// <summary>Gets or sets the UTC time the file was received.</summary>
	public DateTime CreatedUtc { get; set; }
	/// <summary>Gets or sets when uploaded context was removed from future rounds. Historical inputs keep it alive.</summary>
	public DateTime? RemovedUtc { get; set; }
}
