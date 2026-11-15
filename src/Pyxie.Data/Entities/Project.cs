namespace Pyxie.Data.Entities;

/// <summary>A project Pyxie knows about, recorded in the machine database: a folder that holds everything about one repository (its database, its hub, the host side of its card sandboxes, and round logs). At most one project is open at a time, and the pipeline runs for the open one.</summary>
public class Project
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the display name, which is also the name of the project's folder. Unique, ignoring case.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the full path of the project's folder. Unique, ignoring case.</summary>
	public string Folder { get; set; } = string.Empty;

	/// <summary>Gets or sets the folder or URL the project's repository was imported from, without credentials, so the project list can show where a project came from. The repository's own record (<see cref="Repository.Origin"/>) is in the project's database.</summary>
	public string Source { get; set; } = string.Empty;

	/// <summary>Gets or sets the UTC time the project was added.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>Gets or sets a value indicating whether this is the open project. At most one project is open.</summary>
	public bool IsOpen { get; set; }
}
