using Pyxie.Runtime.Git;
using Pyxie.Web.Components.Repository.Navigation;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryBranchSelectionTests
{
	private static readonly DateTime s_time = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Order_PutsTheTargetBranchFirstAndKeepsTheOthersInTheOrderGiven()
	{
		IReadOnlyList<string> ordered = BranchSelection.Order([Branch("feature"), Branch("main"), Branch("bugfix"), Branch("dev")], "main");

		Assert.Equal(["main", "feature", "bugfix", "dev"], ordered);
	}

	[Fact]
	public void Order_KeepsTheOrderGivenWhenTheTargetBranchNoLongerExists()
	{
		IReadOnlyList<string> ordered = BranchSelection.Order([Branch("feature"), Branch("dev")], "main");

		Assert.Equal(["feature", "dev"], ordered);
	}

	[Fact]
	public void Order_DoesNotTreatABranchWhoseNameDiffersOnlyInCaseAsTheTarget()
	{
		IReadOnlyList<string> ordered = BranchSelection.Order([Branch("Main"), Branch("main")], "main");

		Assert.Equal(["main", "Main"], ordered);
	}

	[Fact]
	public void Resolve_PicksTheRequestedBranchWhenItExists()
	{
		Assert.Equal("dev", BranchSelection.Resolve("dev", ["main", "dev"]));
	}

	[Theory]
	[InlineData("")]
	[InlineData("gone")]
	[InlineData("Dev")]
	[InlineData("refs/heads/dev")]
	[InlineData("--upload-pack=x")]
	public void Resolve_PicksTheFirstBranchWhenTheRequestedOneDoesNotExist(string requested)
	{
		Assert.Equal("main", BranchSelection.Resolve(requested, ["main", "dev"]));
	}

	[Fact]
	public void Resolve_ReturnsNothingWhenThereAreNoBranches()
	{
		Assert.Equal(string.Empty, BranchSelection.Resolve("dev", []));
	}

	[Theory]
	[InlineData("main", "refs/heads/main")]
	[InlineData("pyxie/card-3", "refs/heads/pyxie/card-3")]
	public void Revision_NamesTheBranchByItsFullReference(string branch, string expected)
	{
		Assert.Equal(expected, BranchSelection.Revision(branch));
	}

	private static GitBranch Branch(string name)
	{
		return new GitBranch(name, "0123456789abcdef0123456789abcdef01234567", s_time, $"Work on {name}");
	}
}
