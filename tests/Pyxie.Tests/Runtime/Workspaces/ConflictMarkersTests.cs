using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A resolution that still has the markers git wrote is refused, and text that only looks like a marker is not, when a version of the file from before the merge already had the same line.</summary>
public sealed class ConflictMarkersTests
{
	[Theory]
	[InlineData("<<<<<<< HEAD")]
	[InlineData("<<<<<<<")]
	[InlineData(">>>>>>> feature/branch")]
	[InlineData(">>>>>>>")]
	[InlineData("||||||| merged common ancestors")]
	[InlineData("|||||||")]
	[InlineData("=======")]
	[InlineData("=======\r")]
	[InlineData("<<<<<<< HEAD\r\n")]
	public void IsMarker_ForTheLinesGitWrites_IsTrue(string line)
	{
		Assert.True(ConflictMarkers.IsMarker(line), $"{line.ReplaceLineEndings("\\n")} is a conflict marker.");
	}

	[Theory]
	[InlineData("")]
	[InlineData("<<<<<<")]
	[InlineData("<<<<<<<<")]
	[InlineData("<<<<<<<HEAD")]
	[InlineData(">>>>>>>>> text")]
	[InlineData("======")]
	[InlineData("========")]
	[InlineData("======= trailing text")]
	[InlineData(" =======")]
	[InlineData("<<<<<<<=")]
	[InlineData("<>=<>=<")]
	[InlineData("text <<<<<<< HEAD")]
	public void IsMarker_ForLinesThatOnlyLookLikeOne_IsFalse(string line)
	{
		Assert.False(ConflictMarkers.IsMarker(line), $"{line} is not a conflict marker.");
	}

	[Fact]
	public void HasUnresolved_WhenTheFileStillHasTheMarkers_IsTrue()
	{
		string resolved = "before\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> feature\nafter";

		Assert.True(ConflictMarkers.HasUnresolved(resolved, ["before\nours\nafter", "before\ntheirs\nafter"]));
	}

	[Fact]
	public void HasUnresolved_WhenOnlyOneMarkerIsLeft_IsTrue()
	{
		Assert.True(ConflictMarkers.HasUnresolved("kept ours\n=======\nkept theirs", ["kept ours", "kept theirs"]));
	}

	[Fact]
	public void HasUnresolved_WhenTheFileHasNoMarkers_IsFalse()
	{
		Assert.False(ConflictMarkers.HasUnresolved("both lines\nkept", ["ours", "theirs"]));
	}

	[Fact]
	public void HasUnresolved_WhenAMarkerLikeLineIsInAVersionBeforeTheMerge_IsFalse()
	{
		string heading = "Title\n=======\nbody";

		Assert.False(ConflictMarkers.HasUnresolved("Title\n=======\nbody, changed on both sides and resolved", [heading, "Title\n=======\nbody, changed on main"]));
	}

	[Fact]
	public void HasUnresolved_WhenOnlyTheOtherVersionHasTheLine_IsFalse()
	{
		Assert.False(ConflictMarkers.HasUnresolved("a git tutorial\n<<<<<<< HEAD\nshown as an example", [null, "a git tutorial\n<<<<<<< HEAD\nshown as an example, in the target's version"]));
	}

	[Fact]
	public void HasUnresolved_WhenTheLineIsInAVersionOnlyWithADifferentLineEnding_IsStillFalse()
	{
		Assert.False(ConflictMarkers.HasUnresolved("Title\n=======\nbody", ["Title\r\n=======\r\nbody"]));
	}

	[Fact]
	public void HasUnresolved_WhenAVersionHasAnotherMarkerLineThanTheOneLeft_IsTrue()
	{
		Assert.True(ConflictMarkers.HasUnresolved("<<<<<<< HEAD\nours", ["======="]));
	}

	[Fact]
	public void HasUnresolved_WhenAParentHasNoSuchFile_JudgesAgainstTheOtherParentAlone()
	{
		Assert.True(ConflictMarkers.HasUnresolved("=======\nadded on both sides", [null, "added on both sides"]));
		Assert.False(ConflictMarkers.HasUnresolved("=======\nadded on both sides", [null, "=======\nadded on both sides"]));
		Assert.True(ConflictMarkers.HasUnresolved("=======", [null, null]));
	}
}
