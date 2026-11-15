using Pyxie.Web.Components.Repository.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryBreadcrumbTrailTests
{
	[Fact]
	public void Of_ListsEachSegmentWithThePathThatReachesIt()
	{
		IReadOnlyList<BreadcrumbSegment> trail = BreadcrumbTrail.Of("src/app/Program.cs");

		Assert.Equal([new BreadcrumbSegment("src", "src"), new BreadcrumbSegment("app", "src/app"), new BreadcrumbSegment("Program.cs", "src/app/Program.cs")], trail);
	}

	[Fact]
	public void Of_ListsASingleSegmentForAFolderAtTheRoot()
	{
		Assert.Equal([new BreadcrumbSegment("docs", "docs")], BreadcrumbTrail.Of("docs"));
	}

	[Fact]
	public void Of_ListsNothingForTheRoot()
	{
		Assert.Empty(BreadcrumbTrail.Of(string.Empty));
	}

	[Fact]
	public void Of_KeepsSegmentsThatRepeatANameApart()
	{
		Assert.Equal([new BreadcrumbSegment("a", "a"), new BreadcrumbSegment("a", "a/a")], BreadcrumbTrail.Of("a/a"));
	}
}
