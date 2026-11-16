namespace Pyxie.Runtime.Sandboxes;

/// <summary>A request for a project's sandbox image.</summary>
/// <param name="ProjectKey">The project's key (see <see cref="SandboxNames.ProjectKey"/>).</param>
/// <param name="Dockerfile">The project's Dockerfile, which starts <c>FROM</c> Pyxie's base image (see <see cref="Pyxie.Data.Sandboxes.SandboxDefaults.BaseImageArgument"/>).</param>
/// <param name="Rebuild">Whether to build again from scratch even when the image is there, which also brings the agents' command lines up to date.</param>
/// <param name="LogPath">The file the build's output is written to.</param>
public sealed record SandboxImageRequest(string ProjectKey, string Dockerfile, bool Rebuild, string LogPath)
{
	/// <summary>Gets what to call once a build actually begins: after any build ahead of this one ended, and only when the image has to be built. A request whose image is already there never calls it. It runs on the building thread and must not throw.</summary>
	public Action? BuildStarting { get; init; }
}
