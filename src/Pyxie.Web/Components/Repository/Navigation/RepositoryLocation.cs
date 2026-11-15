using System.Globalization;

namespace Pyxie.Web.Components.Repository.Navigation;

/// <summary>Where the Repository page is: the whole page state lives in the address so links and back and forward work. An empty string means "not chosen": the target branch, the repository root, no commit, the first page. Any address, even a hand-edited one, reads as a valid location, and writing a location back leaves out what the tab does not use and what is not chosen.</summary>
/// <param name="Tab">The tab.</param>
/// <param name="Branch">The branch the Files and History tabs show; empty for the target branch.</param>
/// <param name="Path">The folder or file the Files tab shows, from the repository root with forward slashes; empty for the root.</param>
/// <param name="Commit">The hash of the commit the History tab shows in full; empty for the list of commits.</param>
/// <param name="Page">The zero-based page of commits the History tab lists.</param>
public sealed record RepositoryLocation(RepositoryTab Tab, string Branch, string Path, string Commit, int Page)
{
	/// <summary>The address parameter that names the tab.</summary>
	public const string TabKey = "tab";

	/// <summary>The address parameter that names the branch.</summary>
	public const string BranchKey = "branch";

	/// <summary>The address parameter that names the folder or file.</summary>
	public const string PathKey = "path";

	/// <summary>The address parameter that names the commit.</summary>
	public const string CommitKey = "commit";

	/// <summary>The address parameter that names the page of commits.</summary>
	public const string PageKey = "page";

	/// <summary>The most pages of history a location can name, which keeps the commits to skip well inside the range of an integer.</summary>
	public const int MaxPage = 1_000_000;

	private const int MinCommitLength = 4;
	private const int MaxCommitLength = 64;

	/// <summary>Reads a location from the address parameters, repairing whatever does not make sense instead of failing.</summary>
	/// <param name="tab">The tab word, or <see langword="null"/>.</param>
	/// <param name="branch">The branch, or <see langword="null"/>.</param>
	/// <param name="path">The folder or file, or <see langword="null"/>. Empty parts and <c>.</c> are dropped; a path that climbs with <c>..</c> reads as the root.</param>
	/// <param name="commit">The commit hash, or <see langword="null"/>. Anything that is not a hexadecimal hash reads as no commit.</param>
	/// <param name="page">The page number, or <see langword="null"/>. Anything that is not a number from 0 up reads as the first page.</param>
	/// <returns>The location.</returns>
	public static RepositoryLocation FromQuery(string? tab, string? branch, string? path, string? commit, string? page)
	{
		return new RepositoryLocation(RepositoryTabNames.Parse(tab), branch?.Trim() ?? string.Empty, NormalizePath(path), NormalizeCommit(commit), ParsePage(page));
	}

	/// <summary>Gets the address parameters of this location, with <see langword="null"/> for each one the address should not carry.</summary>
	/// <returns>The parameters, by name. The branch is carried by every tab, the path only by Files, the commit and page only by History.</returns>
	public IReadOnlyDictionary<string, object?> ToQuery()
	{
		return new Dictionary<string, object?>
		{
			[TabKey] = Tab == RepositoryTab.Files ? null : RepositoryTabNames.Query(Tab),
			[BranchKey] = Branch.Length == 0 ? null : Branch,
			[PathKey] = Tab == RepositoryTab.Files && Path.Length > 0 ? Path : null,
			[CommitKey] = Tab == RepositoryTab.History && Commit.Length > 0 ? Commit : null,
			[PageKey] = Tab == RepositoryTab.History && Page > 0 ? Page.ToString(CultureInfo.InvariantCulture) : null,
		};
	}

	/// <summary>Moves to another tab. The branch stays; the path, commit, and page belong to a tab and start over.</summary>
	/// <param name="tab">The tab to show.</param>
	/// <returns>The location of that tab.</returns>
	public RepositoryLocation ForTab(RepositoryTab tab)
	{
		return new RepositoryLocation(tab, Branch, string.Empty, string.Empty, 0);
	}

	/// <summary>Moves to another branch. The path stays, so the same folder opens on the other branch; the commit belongs to the old branch, and the page starts over.</summary>
	/// <param name="branch">The branch to show.</param>
	/// <returns>The location on that branch.</returns>
	public RepositoryLocation OnBranch(string branch)
	{
		return this with { Branch = branch, Commit = string.Empty, Page = 0 };
	}

	private static string NormalizePath(string? path)
	{
		List<string> segments = [];

		foreach (string segment in (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment == "..")
			{
				return string.Empty;
			}

			if (segment != ".")
			{
				segments.Add(segment);
			}
		}

		return string.Join('/', segments);
	}

	private static string NormalizeCommit(string? commit)
	{
		string trimmed = commit?.Trim() ?? string.Empty;

		return trimmed.Length is >= MinCommitLength and <= MaxCommitLength && trimmed.All(char.IsAsciiHexDigit) ? trimmed.ToLowerInvariant() : string.Empty;
	}

	private static int ParsePage(string? page)
	{
		return int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? Math.Min(parsed, MaxPage) : 0;
	}
}
