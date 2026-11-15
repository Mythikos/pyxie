namespace Pyxie.Data.Entities;

/// <summary>How a project's sandboxes are made (see <c>docs/sandboxing.md</c>): the Dockerfile of the project's image. A project database holds at most one; until it is saved, the defaults apply (<see cref="Sandboxes.SandboxDefaults"/>).</summary>
public class SandboxSettings
{
	/// <summary>Gets or sets the database identifier; 0 for settings that were never saved.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the project image's Dockerfile, which starts from Pyxie's base image.</summary>
	public string Dockerfile { get; set; } = string.Empty;

	/// <summary>Gets or sets the UTC time the settings were last saved.</summary>
	public DateTime UpdatedUtc { get; set; }

	/// <summary>Gets or sets the version, incremented by every save; 0 for settings that were never saved. A save only lands while the version is the one the person read.</summary>
	public int Version { get; set; }
}
