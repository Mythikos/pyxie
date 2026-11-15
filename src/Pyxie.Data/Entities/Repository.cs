using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>The git repository a project works on: the hub, Pyxie's own bare clone of a folder or URL in the project's folder. Each project database holds exactly one, written at import.</summary>
public class Repository
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets where the repository was imported from.</summary>
	public RepositorySource Source { get; set; }

	/// <summary>Gets or sets the absolute path of the hub in the project's folder. Everything the pipeline writes goes here.</summary>
	public string Path { get; set; } = string.Empty;

	/// <summary>Gets or sets the folder or URL the clone was made from, which stays the clone's <c>origin</c>.</summary>
	public string Origin { get; set; } = string.Empty;

	/// <summary>Gets or sets the branch finished cards are merged into, and that new card branches start from: the branch checked out in the source when it was imported.</summary>
	public string TargetBranch { get; set; } = string.Empty;

	/// <summary>Gets or sets the UTC time the repository was imported.</summary>
	public DateTime ImportedUtc { get; set; }
}
