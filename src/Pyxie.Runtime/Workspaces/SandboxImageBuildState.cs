namespace Pyxie.Runtime.Workspaces;

/// <summary>Where a build of a project's sandbox image stands.</summary>
public enum SandboxImageBuildState
{
	/// <summary>It is building, or a person's build is waiting for the build ahead of it.</summary>
	Running = 0,

	/// <summary>The image was built.</summary>
	Succeeded = 1,

	/// <summary>The image could not be built, or Docker did not answer.</summary>
	Failed = 2,

	/// <summary>A person cancelled it, the pipeline stopped, or Pyxie shut down; no image was made.</summary>
	Cancelled = 3,
}
