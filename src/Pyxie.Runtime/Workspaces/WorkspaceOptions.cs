namespace Pyxie.Runtime.Workspaces;

/// <summary>Where Pyxie keeps its data: the machine database, the sign-ins agents' sandboxes get, and the project folders. Bound from the <c>Workspace</c> configuration section.</summary>
public sealed class WorkspaceOptions
{
	/// <summary>The configuration section the options are read from.</summary>
	public const string SectionName = "Workspace";

	/// <summary>
	/// Gets or sets the folder that holds everything, outside every repository so a harness never loads another project's instructions. Empty means
	/// <c>%LOCALAPPDATA%\Pyxie</c> (or the platform's equivalent).
	/// </summary>
	public string Root { get; set; } = string.Empty;
}
