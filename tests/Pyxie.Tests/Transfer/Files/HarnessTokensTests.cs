using Pyxie.Data.Enums;
using Pyxie.Data.Transfer.Files;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>A settings file names a harness in its own vocabulary, never by the enum's member names or numbers. The version 2 names are pinned here; renaming one breaks every file already exported, so it takes a new file version instead. Every harness has exactly one name, and a name is read exactly as written.</summary>
public sealed class HarnessTokensTests
{
	public static TheoryData<HarnessKind> Harnesses { get; } = [.. Enum.GetValues<HarnessKind>()];

	[Theory]
	[InlineData(HarnessKind.ClaudeCode, "claudeCode")]
	[InlineData(HarnessKind.Codex, "codex")]
	[InlineData(HarnessKind.DeepSeek, "deepSeek")]
	[InlineData(HarnessKind.Other, "other")]
	public void ToToken_WritesTheVersion2NameOfTheHarness(HarnessKind harness, string token)
	{
		Assert.Equal(token, HarnessTokens.ToToken(harness));
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode, "claudeCode")]
	[InlineData(HarnessKind.Codex, "codex")]
	[InlineData(HarnessKind.DeepSeek, "deepSeek")]
	[InlineData(HarnessKind.Other, "other")]
	public void TryParse_ReadsTheVersion2NameAsItsHarness(HarnessKind harness, string token)
	{
		Assert.True(HarnessTokens.TryParse(token, out HarnessKind parsed), $"\"{token}\" is the version 2 name of {harness}.");
		Assert.Equal(harness, parsed);
	}

	[Fact]
	public void All_ListsTheVersion2NamesInTheOrderTheHarnessesAreOffered()
	{
		Assert.Equal(["claudeCode", "codex", "deepSeek", "other"], HarnessTokens.All);
	}

	[Theory]
	[MemberData(nameof(Harnesses))]
	public void EveryHarness_HasExactlyOneNameThatReadsBackAsIt(HarnessKind harness)
	{
		string token = HarnessTokens.ToToken(harness);

		Assert.True(HarnessTokens.All.Count(name => name == token) == 1, $"{harness} is written as \"{token}\", which must be exactly one of the names a settings file uses. A new harness needs a name in HarnessTokens, and a new file version (SettingsFileFormat).");
		Assert.True(HarnessTokens.TryParse(token, out HarnessKind parsed) && parsed == harness, $"\"{token}\" must read back as {harness}.");
	}

	[Fact]
	public void All_HasOneNamePerHarnessAndNoOthers()
	{
		Assert.True(HarnessTokens.All.Count == Enum.GetValues<HarnessKind>().Length, $"Every name must belong to a harness: {string.Join(", ", HarnessTokens.All)} for {string.Join(", ", Enum.GetValues<HarnessKind>())}.");
		Assert.True(HarnessTokens.All.Distinct(StringComparer.OrdinalIgnoreCase).Count() == HarnessTokens.All.Count, "No two harness names may differ only in case, or a file written by hand could mean either.");
	}

	[Theory]
	[InlineData("ClaudeCode")]
	[InlineData("claudecode")]
	[InlineData("CODEX")]
	[InlineData(" codex")]
	[InlineData("codex ")]
	[InlineData("0")]
	[InlineData("3")]
	[InlineData("")]
	[InlineData(null)]
	public void TryParse_RefusesAnythingButANameExactlyAsWritten(string? token)
	{
		Assert.False(HarnessTokens.TryParse(token, out HarnessKind parsed), $"\"{token}\" is not a harness name as a settings file writes it, so it must not be read as {parsed}.");
	}

	[Theory]
	[InlineData(999, "999")]
	[InlineData(42, "42")]
	[InlineData(-1, "-1")]
	public void ToToken_OfAValueNoHarnessHas_WritesItsNumberSoAnExportNeverFails(int value, string expected)
	{
		Assert.Equal(expected, HarnessTokens.ToToken((HarnessKind)value));
	}
}
