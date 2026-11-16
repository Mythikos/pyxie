namespace Pyxie.Runtime.Workspaces;

/// <summary>Who asked for a build of a project's sandbox image.</summary>
public enum SandboxImageBuildRequester
{
	/// <summary>A person, with Build in Settings: a rebuild from scratch that they can cancel.</summary>
	Person = 0,

	/// <summary>The pipeline, for a round whose image was not there yet. Stopping the pipeline stops it.</summary>
	Pipeline = 1,
}
