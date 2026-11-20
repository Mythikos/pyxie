namespace Pyxie.Runtime.Media;

/// <summary>Symmetric sandbox media paths and private host storage paths.</summary>
public static class MediaPaths
{
	/// <summary>The read-only manifest supplied to an agent.</summary>
	public const string InputManifest = "/pyxie/in/media/manifest.json";
	/// <summary>The manifest an agent publishes.</summary>
	public const string OutputManifest = "/pyxie/out/media/manifest.json";
	/// <summary>Gets the private media root.</summary>
	/// <param name="projectFolder">The owning project folder.</param>
	/// <returns>The absolute media folder.</returns>
	public static string Root(string projectFolder) => Path.Combine(projectFolder, "media");
	/// <summary>Gets an immutable object's location.</summary>
	/// <param name="projectFolder">The owning project folder.</param>
	/// <param name="id">The server-assigned file identifier.</param>
	/// <returns>The absolute file path.</returns>
	public static string Object(string projectFolder, Guid id) => Path.Combine(Root(projectFolder), "objects", id.ToString("N"));
	/// <summary>Gets a host-owned receipt of a round's collected files.</summary>
	/// <param name="projectFolder">The owning project folder.</param>
	/// <param name="roundId">The producing round.</param>
	/// <returns>The receipt path.</returns>
	public static string Receipt(string projectFolder, int roundId) => Path.Combine(Root(projectFolder), "staging", $"round-{roundId}.json");
}
