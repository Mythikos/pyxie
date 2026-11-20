namespace Pyxie.Data.Media;

/// <summary>The context selection saved atomically with a card.</summary>
/// <param name="UploadId">The editor's session, bound to the original card and version.</param>
/// <param name="FileIds">The complete desired context selection, including retained and newly uploaded files.</param>
public sealed record MediaEdit(Guid UploadId, IReadOnlyList<Guid> FileIds);
