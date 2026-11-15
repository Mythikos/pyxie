using Pyxie.Web.Components.Repository.Diffs;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryDiffParserTests
{
	[Fact]
	public void Parse_ClassifiesTheHeaderHunkContextRemovedAndAddedLines()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			index 1111111..2222222 100644
			--- a/a.txt
			+++ b/a.txt
			@@ -1,3 +1,3 @@
			 keep
			-old
			+new
			 end
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal(
			[
				new DiffBlock(DiffBlockKind.Header, "diff --git a/a.txt b/a.txt\nindex 1111111..2222222 100644\n--- a/a.txt\n+++ b/a.txt"),
				new DiffBlock(DiffBlockKind.Hunk, "@@ -1,3 +1,3 @@"),
				new DiffBlock(DiffBlockKind.Context, " keep"),
				new DiffBlock(DiffBlockKind.Removed, "-old"),
				new DiffBlock(DiffBlockKind.Added, "+new"),
				new DiffBlock(DiffBlockKind.Context, " end"),
			],
			blocks);
	}

	[Fact]
	public void Parse_GroupsConsecutiveLinesOfOneKindIntoOneBlock()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1,3 +1,3 @@
			-one
			-two
			-three
			+uno
			+dos
			+tres
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added], blocks.Select(block => block.Kind));
		Assert.Equal("-one\n-two\n-three", blocks[2].Text);
		Assert.Equal("+uno\n+dos\n+tres", blocks[3].Text);
	}

	[Fact]
	public void Parse_ReadsARemovedLineThatLooksLikeAFileHeaderAsRemoved()
	{
		const string patch = """
			diff --git a/s.sql b/s.sql
			--- a/s.sql
			+++ b/s.sql
			@@ -1,2 +1,2 @@
			--- a comment
			+++ b comment
			 tail
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added, DiffBlockKind.Context], blocks.Select(block => block.Kind));
		Assert.Equal("--- a comment", blocks[2].Text);
		Assert.Equal("+++ b comment", blocks[3].Text);
	}

	[Fact]
	public void Parse_ReadsTheFileNamesBeforeTheFirstHunkAsHeaderEvenThoughTheyStartWithMinusAndPlus()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1 +1 @@
			-x
			+y
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal(DiffBlockKind.Header, blocks[0].Kind);
		Assert.Contains("--- a/a.txt", blocks[0].Text, StringComparison.Ordinal);
		Assert.Contains("+++ b/a.txt", blocks[0].Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_StartsANewBlockForEachFileEvenWithoutAHunkBetween()
	{
		const string patch = """
			diff --git a/img.png b/img.png
			index 1111111..2222222 100644
			Binary files a/img.png and b/img.png differ
			diff --git a/b.txt b/b.txt
			--- a/b.txt
			+++ b/b.txt
			@@ -1 +1 @@
			-x
			+y
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added], blocks.Select(block => block.Kind));
		Assert.Equal("diff --git a/img.png b/img.png\nindex 1111111..2222222 100644\nBinary files a/img.png and b/img.png differ", blocks[0].Text);
		Assert.StartsWith("diff --git a/b.txt b/b.txt", blocks[1].Text, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_ReadsTheHeaderLinesOfARenameAndAModeChangeAsHeader()
	{
		const string patch = """
			diff --git a/old.txt b/new.txt
			similarity index 100%
			rename from old.txt
			rename to new.txt
			diff --git a/run.sh b/run.sh
			old mode 100644
			new mode 100755
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Header], blocks.Select(block => block.Kind));
	}

	[Fact]
	public void Parse_MarksTheNoNewlineRemarkAsANoteWithoutCountingItAsALine()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1 +1 @@
			-a
			\ No newline at end of file
			+b
			\ No newline at end of file
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Note, DiffBlockKind.Added, DiffBlockKind.Note], blocks.Select(block => block.Kind));
	}

	[Fact]
	public void Parse_ReadsAHunkHeaderWithTheEnclosingMethodAfterItAsOneHunkLine()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -3 +3 @@ void Method()
			--removed
			++added
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added], blocks.Select(block => block.Kind));
		Assert.Equal("@@ -3 +3 @@ void Method()", blocks[1].Text);
	}

	[Fact]
	public void Parse_ReadsABodyLineThatContainsAHunkOrFileHeaderByItsFirstCharacter()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1,3 +1,3 @@
			-diff --git a/x b/x
			+@@ -1 +1 @@
			 @@ context
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added, DiffBlockKind.Context], blocks.Select(block => block.Kind));
	}

	[Fact]
	public void Parse_ReadsAnEmptyLineInsideAHunkAsContext()
	{
		const string patch = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1,3 +1,3 @@\n a\n\n c";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Context], blocks.Select(block => block.Kind));
		Assert.Equal(" a\n\n c", blocks[2].Text);
	}

	[Fact]
	public void Parse_ReadsADiffLineAfterAHunkBodyAsTheNextFile()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1,5 +1,5 @@
			 only one line
			diff --git a/b.txt b/b.txt
			--- a/b.txt
			+++ b/b.txt
			@@ -1 +1 @@
			-x
			+y
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Context, DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added], blocks.Select(block => block.Kind));
	}

	[Fact]
	public void Parse_MarksTheTruncationMarkerAsANoteAndReadsTheLinesAfterItByTheirFirstCharacter()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1,9 +1,9 @@
			+kept
			[truncated: 120 characters omitted]
			rest of a cut line
			+added after
			-removed after
			 context after
			diff --git a/b.txt b/b.txt
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal(
			[
				DiffBlockKind.Header,
				DiffBlockKind.Hunk,
				DiffBlockKind.Added,
				DiffBlockKind.Note,
				DiffBlockKind.Context,
				DiffBlockKind.Added,
				DiffBlockKind.Removed,
				DiffBlockKind.Context,
				DiffBlockKind.Header,
			],
			blocks.Select(block => block.Kind));
		Assert.Equal("[truncated: 120 characters omitted]", blocks[3].Text);
	}

	[Fact]
	public void Parse_ReadsLinesAfterATruncationMarkerThatInterruptedAHeaderByTheirFirstCharacter()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			index 1111111..2222222 100644
			[truncated: 99 characters omitted]
			+added
			 context
			@@ -5 +5 @@
			-gone
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Note, DiffBlockKind.Added, DiffBlockKind.Context, DiffBlockKind.Hunk, DiffBlockKind.Removed], blocks.Select(block => block.Kind));
	}

	[Fact]
	public void Parse_SplitsATruncationMarkerGluedToTheEndOfAHalfLine()
	{
		const string patch = """
			diff --git a/a.txt b/a.txt
			--- a/a.txt
			+++ b/a.txt
			@@ -1,9 +1,9 @@
			+half a li[truncated: 42 characters omitted]
			ne of text
			""";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Added, DiffBlockKind.Note, DiffBlockKind.Context], blocks.Select(block => block.Kind));
		Assert.Equal("+half a li", blocks[2].Text);
		Assert.Equal("[truncated: 42 characters omitted]", blocks[3].Text);
	}

	[Theory]
	[InlineData("+a line that merely mentions [truncated: many characters omitted]")]
	[InlineData("+[truncated:  characters omitted]")]
	[InlineData("+[truncated: 5 characters omitted] and more")]
	public void Parse_KeepsAnAddedLineThatOnlyLooksLikeTheTruncationMarker(string line)
	{
		string patch = $"diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -0,0 +1 @@\n{line}";

		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Added], blocks.Select(block => block.Kind));
		Assert.Equal(line, blocks[2].Text);
	}

	[Fact]
	public void Parse_ReadsLinesOutsideAnyHunkByTheirFirstCharacter()
	{
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse("+added\n-removed\n context\nplain");

		Assert.Equal([DiffBlockKind.Added, DiffBlockKind.Removed, DiffBlockKind.Context], blocks.Select(block => block.Kind));
		Assert.Equal(" context\nplain", blocks[2].Text);
	}

	[Fact]
	public void Parse_DropsCarriageReturnsFromTheEndsOfLines()
	{
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse("diff --git a/a.txt b/a.txt\r\n--- a/a.txt\r\n+++ b/a.txt\r\n@@ -1 +1 @@\r\n-x\r\n+y\r\n");

		Assert.DoesNotContain(blocks, block => block.Text.Contains('\r', StringComparison.Ordinal));
		Assert.Equal("+y", blocks[^1].Text);
	}

	[Theory]
	[InlineData("")]
	[InlineData("\n")]
	public void Parse_ReturnsNoBlocksForAnEmptyDiff(string patch)
	{
		Assert.Empty(DiffParser.Parse(patch));
	}

	[Fact]
	public void Parse_DoesNotAddABlockForTheLineBreakThatEndsTheDiff()
	{
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse("diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-x\n+y\n");

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Removed, DiffBlockKind.Added], blocks.Select(block => block.Kind));
	}
}
