namespace Pyxie.Data.Entities;

/// <summary>An immutable record of one file supplied to a round.</summary>
public sealed class RoundMediaInput
{
	/// <summary>Gets or sets the receiving round.</summary>
	public int RoundId { get; set; }
	/// <summary>Gets or sets the supplied file.</summary>
	public Guid MediaFileId { get; set; }
	/// <summary>Gets or sets the file's metadata.</summary>
	public MediaFile File { get; set; } = new();
}
