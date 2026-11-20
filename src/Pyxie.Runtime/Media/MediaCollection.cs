using Pyxie.Data.Entities;

namespace Pyxie.Runtime.Media;

/// <summary>Durable media and bounded collection problems, independent of the agent's work verdict.</summary>
/// <param name="Files">Files copied into the project's private object store.</param>
/// <param name="Problems">Reasons individual publications could not be attached.</param>
/// <param name="Complete">Whether the manifest was fully examined.</param>
public sealed record MediaCollection(IReadOnlyList<MediaFile> Files, IReadOnlyList<string> Problems, bool Complete = false)
{
	/// <summary>An empty hand-out.</summary>
	public static readonly MediaCollection Empty = new([], []);
}
