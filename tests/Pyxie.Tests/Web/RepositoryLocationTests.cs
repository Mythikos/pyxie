using Pyxie.Web.Components.Repository.Navigation;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryLocationTests
{
	[Fact]
	public void FromQuery_ReadsEveryParameter()
	{
		RepositoryLocation location = RepositoryLocation.FromQuery("history", "feature/x", "src/a", "ABCDEF1", "3");

		Assert.Equal(new RepositoryLocation(RepositoryTab.History, "feature/x", "src/a", "abcdef1", 3), location);
	}

	[Fact]
	public void FromQuery_ReadsMissingParametersAsTheFilesTabAtTheRootOfTheTargetBranch()
	{
		RepositoryLocation location = RepositoryLocation.FromQuery(null, null, null, null, null);

		Assert.Equal(new RepositoryLocation(RepositoryTab.Files, string.Empty, string.Empty, string.Empty, 0), location);
	}

	[Theory]
	[InlineData("files", RepositoryTab.Files)]
	[InlineData("history", RepositoryTab.History)]
	[InlineData("branches", RepositoryTab.Branches)]
	[InlineData("HISTORY", RepositoryTab.History)]
	[InlineData(" branches ", RepositoryTab.Branches)]
	[InlineData("", RepositoryTab.Files)]
	[InlineData("settings", RepositoryTab.Files)]
	[InlineData(null, RepositoryTab.Files)]
	public void FromQuery_ReadsTheTabWordInAnyCaseAndFallsBackToFiles(string? word, RepositoryTab expected)
	{
		Assert.Equal(expected, RepositoryLocation.FromQuery(word, null, null, null, null).Tab);
	}

	[Theory]
	[InlineData("src/app", "src/app")]
	[InlineData("a//b/", "a/b")]
	[InlineData("/a/b", "a/b")]
	[InlineData("./a/./b", "a/b")]
	[InlineData("src/My Folder/x.cs", "src/My Folder/x.cs")]
	[InlineData("a/../b", "")]
	[InlineData("..", "")]
	[InlineData("a/b/..", "")]
	[InlineData("/", "")]
	[InlineData("", "")]
	[InlineData(null, "")]
	public void FromQuery_NormalizesThePathAndNeverLetsItClimbOutOfTheRepository(string? path, string expected)
	{
		Assert.Equal(expected, RepositoryLocation.FromQuery(null, null, path, null, null).Path);
	}

	[Theory]
	[InlineData("abcd", "abcd")]
	[InlineData("ABCDEF12", "abcdef12")]
	[InlineData(" 0123abcd ", "0123abcd")]
	[InlineData("abc", "")]
	[InlineData("xyz1234", "")]
	[InlineData("abcd efgh", "")]
	[InlineData("abcd;rm", "")]
	[InlineData("--output=x", "")]
	[InlineData("", "")]
	[InlineData(null, "")]
	public void FromQuery_KeepsOnlyAHexadecimalHashAsTheCommit(string? commit, string expected)
	{
		Assert.Equal(expected, RepositoryLocation.FromQuery(null, null, null, commit, null).Commit);
	}

	[Fact]
	public void FromQuery_AcceptsAHashOfSixtyFourCharactersButNotOneOfSixtyFive()
	{
		string longest = new('a', 64);

		Assert.Equal(longest, RepositoryLocation.FromQuery(null, null, null, longest, null).Commit);
		Assert.Equal(string.Empty, RepositoryLocation.FromQuery(null, null, null, longest + "a", null).Commit);
	}

	[Theory]
	[InlineData("0", 0)]
	[InlineData("2", 2)]
	[InlineData("-1", 0)]
	[InlineData("+3", 0)]
	[InlineData("1.5", 0)]
	[InlineData("x", 0)]
	[InlineData("", 0)]
	[InlineData(null, 0)]
	[InlineData("99999999999", 0)]
	[InlineData("5000000", RepositoryLocation.MaxPage)]
	public void FromQuery_ReadsThePageAsANumberFromZeroUpAndCapsIt(string? page, int expected)
	{
		Assert.Equal(expected, RepositoryLocation.FromQuery(null, null, null, null, page).Page);
	}

	[Fact]
	public void ToQuery_WritesTheFilesTabWithItsBranchAndPathOnly()
	{
		RepositoryLocation location = new(RepositoryTab.Files, "dev", "src/app", "abcdef1", 4);

		IReadOnlyDictionary<string, object?> query = location.ToQuery();

		Assert.Null(query[RepositoryLocation.TabKey]);
		Assert.Equal("dev", query[RepositoryLocation.BranchKey]);
		Assert.Equal("src/app", query[RepositoryLocation.PathKey]);
		Assert.Null(query[RepositoryLocation.CommitKey]);
		Assert.Null(query[RepositoryLocation.PageKey]);
	}

	[Fact]
	public void ToQuery_WritesTheHistoryTabWithItsBranchCommitAndPageOnly()
	{
		RepositoryLocation location = new(RepositoryTab.History, "dev", "src/app", "abcdef1", 4);

		IReadOnlyDictionary<string, object?> query = location.ToQuery();

		Assert.Equal("history", query[RepositoryLocation.TabKey]);
		Assert.Equal("dev", query[RepositoryLocation.BranchKey]);
		Assert.Null(query[RepositoryLocation.PathKey]);
		Assert.Equal("abcdef1", query[RepositoryLocation.CommitKey]);
		Assert.Equal("4", query[RepositoryLocation.PageKey]);
	}

	[Fact]
	public void ToQuery_WritesTheBranchesTabWithItsBranchOnly()
	{
		RepositoryLocation location = new(RepositoryTab.Branches, "dev", "src/app", "abcdef1", 4);

		IReadOnlyDictionary<string, object?> query = location.ToQuery();

		Assert.Equal("branches", query[RepositoryLocation.TabKey]);
		Assert.Equal("dev", query[RepositoryLocation.BranchKey]);
		Assert.Null(query[RepositoryLocation.PathKey]);
		Assert.Null(query[RepositoryLocation.CommitKey]);
		Assert.Null(query[RepositoryLocation.PageKey]);
	}

	[Fact]
	public void ToQuery_LeavesOutEverythingThatIsNotChosen()
	{
		IReadOnlyDictionary<string, object?> query = new RepositoryLocation(RepositoryTab.History, string.Empty, string.Empty, string.Empty, 0).ToQuery();

		Assert.Equal("history", query[RepositoryLocation.TabKey]);
		Assert.Null(query[RepositoryLocation.BranchKey]);
		Assert.Null(query[RepositoryLocation.CommitKey]);
		Assert.Null(query[RepositoryLocation.PageKey]);
	}

	[Fact]
	public void ToQuery_NamesEveryParameterSoANavigationRemovesTheOnesItDoesNotUse()
	{
		IReadOnlyDictionary<string, object?> query = new RepositoryLocation(RepositoryTab.Files, string.Empty, string.Empty, string.Empty, 0).ToQuery();

		Assert.Equal(["branch", "commit", "page", "path", "tab"], query.Keys.Order(StringComparer.Ordinal));
	}

	[Theory]
	[InlineData(RepositoryTab.Files, "main", "src/a.cs", "", 0)]
	[InlineData(RepositoryTab.Files, "pyxie/card-7", "a b/c&d=e.txt", "", 0)]
	[InlineData(RepositoryTab.History, "dev", "", "abcdef12", 3)]
	[InlineData(RepositoryTab.History, "", "", "", 0)]
	[InlineData(RepositoryTab.Branches, "dev", "", "", 0)]
	public void ToQuery_ThenFromQuery_ReturnsTheSameLocation(RepositoryTab tab, string branch, string path, string commit, int page)
	{
		RepositoryLocation location = new(tab, branch, path, commit, page);
		IReadOnlyDictionary<string, object?> query = location.ToQuery();

		RepositoryLocation read = RepositoryLocation.FromQuery(query[RepositoryLocation.TabKey]?.ToString(), query[RepositoryLocation.BranchKey]?.ToString(), query[RepositoryLocation.PathKey]?.ToString(), query[RepositoryLocation.CommitKey]?.ToString(), query[RepositoryLocation.PageKey]?.ToString());

		Assert.Equal(location, read);
	}

	[Fact]
	public void ForTab_KeepsTheBranchAndStartsThePathCommitAndPageOver()
	{
		RepositoryLocation location = new RepositoryLocation(RepositoryTab.History, "dev", "src", "abcdef1", 4).ForTab(RepositoryTab.Files);

		Assert.Equal(new RepositoryLocation(RepositoryTab.Files, "dev", string.Empty, string.Empty, 0), location);
	}

	[Fact]
	public void OnBranch_KeepsTheTabAndPathButDropsTheCommitAndPage()
	{
		RepositoryLocation location = new RepositoryLocation(RepositoryTab.Files, "dev", "src", "abcdef1", 4).OnBranch("main");

		Assert.Equal(new RepositoryLocation(RepositoryTab.Files, "main", "src", string.Empty, 0), location);
	}
}
