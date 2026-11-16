namespace Pyxie.Runtime.Workspaces;

/// <summary>One build of a project's sandbox image, as <see cref="SandboxImageBuilds"/> reports it. A new value is made each time it changes; <see cref="Id"/> tells which build it is.</summary>
/// <param name="Id">Tells this build from every other since Pyxie started.</param>
/// <param name="ProjectKey">The key of the project whose image it builds.</param>
/// <param name="Dockerfile">The Dockerfile it builds.</param>
/// <param name="RequestedBy">Who asked for it.</param>
/// <param name="State">Where it stands.</param>
/// <param name="StartedUtc">When it was asked for, or for the pipeline's, when it began building.</param>
/// <param name="EndedUtc">When it ended; <see langword="null"/> while it runs.</param>
/// <param name="Tag">The tag of the image it built, once it succeeded.</param>
/// <param name="Error">Why it failed, once it failed.</param>
/// <param name="LogPath">The file the build writes its output to.</param>
/// <param name="LogTail">The end of that output once it failed, which is where docker says what went wrong; <see langword="null"/> otherwise.</param>
public sealed record SandboxImageBuild(int Id, string ProjectKey, string Dockerfile, SandboxImageBuildRequester RequestedBy, SandboxImageBuildState State, DateTime StartedUtc, DateTime? EndedUtc, string? Tag, string? Error, string LogPath, string? LogTail)
{
	/// <summary>Gets a value indicating whether it is still running.</summary>
	public bool IsRunning => State == SandboxImageBuildState.Running;

	/// <summary>Tells whether this build is of a project's image for a Dockerfile, line endings aside, which is how the image's tag is reckoned too.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="dockerfile">The Dockerfile.</param>
	/// <returns><see langword="true"/> when it builds that project's image from that Dockerfile.</returns>
	public bool Builds(string projectKey, string dockerfile)
	{
		return ProjectKey == projectKey && Dockerfile.ReplaceLineEndings("\n") == dockerfile.ReplaceLineEndings("\n");
	}
}
