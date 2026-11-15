using Pyxie.Web.Components.Repository.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryFileTextTests
{
	[Theory]
	[InlineData("", 0)]
	[InlineData("one", 1)]
	[InlineData("one\n", 1)]
	[InlineData("one\ntwo", 2)]
	[InlineData("one\ntwo\n", 2)]
	[InlineData("one\n\nthree", 3)]
	[InlineData("\n", 1)]
	[InlineData("\n\n", 2)]
	public void LineCount_CountsLinesAndLetsAFinalLineBreakEndTheLastLine(string text, int expected)
	{
		Assert.Equal(expected, FileText.LineCount(text));
	}

	[Theory]
	[InlineData(0, "")]
	[InlineData(1, "1")]
	[InlineData(3, "1\n2\n3")]
	public void Gutter_NumbersTheLinesFromOneSeparatedByLineBreaks(int lineCount, string expected)
	{
		Assert.Equal(expected, FileText.Gutter(lineCount));
	}

	[Fact]
	public void Gutter_HasOneEntryPerLineOfTheText()
	{
		const string text = "a\nb\n\nd\n";

		Assert.Equal(FileText.LineCount(text), FileText.Gutter(FileText.LineCount(text)).Split('\n').Length);
	}
}
