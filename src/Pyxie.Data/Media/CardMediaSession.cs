using Pyxie.Data.Entities;

namespace Pyxie.Data.Media;

/// <summary>A new editor session and the current files it can retain.</summary>
/// <param name="UploadId">The opaque upload session.</param>
/// <param name="Files">The current context selection.</param>
public sealed record CardMediaSession(Guid UploadId, IReadOnlyList<MediaFile> Files);
