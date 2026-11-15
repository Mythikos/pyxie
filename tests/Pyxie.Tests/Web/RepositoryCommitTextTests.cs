using Pyxie.Web.Components.Repository.Display;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryCommitTextTests
{
	[Theory]
	[InlineData("0123456789abcdef", 7, "0123456")]
	[InlineData("0123456789abcdef", 10, "0123456789")]
	[InlineData("0123", 7, "0123")]
	[InlineData("", 7, "")]
	public void Abbreviate_KeepsTheFirstCharactersOfTheHash(string sha, int length, string expected)
	{
		Assert.Equal(expected, CommitText.Abbreviate(sha, length));
	}

	[Theory]
	[InlineData("Vince", "V")]
	[InlineData("vince", "V")]
	[InlineData("  ada lovelace", "A")]
	[InlineData("émile", "É")]
	[InlineData("Pyxie", "P")]
	[InlineData("", "?")]
	[InlineData("   ", "?")]
	public void Initial_IsTheFirstLetterOfTheNameInUpperCase(string name, string expected)
	{
		Assert.Equal(expected, CommitText.Initial(name));
	}

	[Fact]
	public void Initial_KeepsACharacterMadeOfSeveralCodeUnitsWhole()
	{
		Assert.Equal("😀", CommitText.Initial("😀 smile"));
	}

	[Fact]
	public void Split_SeparatesTheSubjectFromTheBodyAtTheBlankLine()
	{
		(string subject, string body) = CommitText.Split("Add the thing\n\nIt does X.\nAnd Y.");

		Assert.Equal("Add the thing", subject);
		Assert.Equal("It does X.\nAnd Y.", body);
	}

	[Fact]
	public void Split_GivesAnEmptyBodyToAOneLineMessage()
	{
		(string subject, string body) = CommitText.Split("Add the thing");

		Assert.Equal("Add the thing", subject);
		Assert.Equal(string.Empty, body);
	}

	[Fact]
	public void Split_ReadsWindowsLineEndings()
	{
		(string subject, string body) = CommitText.Split("Add the thing\r\n\r\nDetails\r\nmore");

		Assert.Equal("Add the thing", subject);
		Assert.Equal("Details\nmore", body);
	}

	[Fact]
	public void Split_KeepsTheIndentationOfTheFirstBodyLine()
	{
		(_, string body) = CommitText.Split("Subject\n\n    indented\nplain");

		Assert.Equal("    indented\nplain", body);
	}

	[Fact]
	public void Split_ReadsAnEmptyMessageAsAnEmptySubjectAndBody()
	{
		Assert.Equal((string.Empty, string.Empty), CommitText.Split(string.Empty));
	}
}
