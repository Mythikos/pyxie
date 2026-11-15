namespace Pyxie.Web.Components.Repository.Navigation;

/// <summary>Maps the tabs of the Repository page to the words that name them in the address and on screen. The address words are fixed so links keep working when the enum changes.</summary>
public static class RepositoryTabNames
{
	/// <summary>Gets every tab in display order.</summary>
	public static IReadOnlyList<RepositoryTab> All { get; } = [RepositoryTab.Files, RepositoryTab.History, RepositoryTab.Branches];

	/// <summary>Gets the word that names a tab in the address.</summary>
	/// <param name="tab">The tab.</param>
	/// <returns>A lower-case word.</returns>
	public static string Query(RepositoryTab tab) => tab switch
	{
		RepositoryTab.History => "history",
		RepositoryTab.Branches => "branches",
		_ => "files",
	};

	/// <summary>Gets the label shown on a tab.</summary>
	/// <param name="tab">The tab.</param>
	/// <returns>The label.</returns>
	public static string Label(RepositoryTab tab) => tab switch
	{
		RepositoryTab.History => "History",
		RepositoryTab.Branches => "Branches",
		_ => "Files",
	};

	/// <summary>Reads a tab from the address.</summary>
	/// <param name="query">The word from the address, in any letter case, or <see langword="null"/>.</param>
	/// <returns>The named tab; <see cref="RepositoryTab.Files"/> for a missing or unknown word.</returns>
	public static RepositoryTab Parse(string? query)
	{
		foreach (RepositoryTab tab in All)
		{
			if (string.Equals(Query(tab), query?.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return tab;
			}
		}

		return RepositoryTab.Files;
	}
}
