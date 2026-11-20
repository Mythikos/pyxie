using Pyxie.Data.Colors;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

using static Pyxie.Tests.Transfer.Files.SettingsFileText;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>Every item of a version 1 file has every member version 1 names, a harness by one of the file's harness names, and a color written <c>#RRGGBB</c>. The reader names an item by its name, trimmed, or by its place in its list when it has none, and, in a file shaped as version 1, lists every such problem, one per line, instead of stopping at the first.</summary>
public sealed class SettingsFileReaderItemTests
{
	private const string Harnesses = "The harnesses are claudeCode, codex, openCode, and other.";

	public static IEnumerable<TheoryDataRow<string, string>> MissingMembers { get; } =
	[
		.. new[] { "harness", "command", "arguments", "canWork", "canReview", "canMerge" }.Select(member => Case($"an agent without {member}", FileWith(Section("agents", Agent("Claude", without: member))), $"Agent 'Claude' has no \"{member}\".")),
		Case("the second agent without a name", FileWith(Section("agents", Agent("Codex"), Agent("Claude", without: "name"))), "Agent 2 has no \"name\"."),
		.. new[] { "command", "arguments", "timeoutMinutes", "enabled" }.Select(member => Case($"a gate without {member}", FileWith(Section("gates", Gate("Build", without: member))), $"Gate 'Build' has no \"{member}\".")),
		Case("the third gate without a name", FileWith(Section("gates", Gate("Build"), Gate("Tests"), Gate("Lint", without: "name"))), "Gate 3 has no \"name\"."),
		Case("a category without a color", FileWith(Section("categories", Category("Bug", without: "color"))), "Category 'Bug' has no \"color\"."),
		Case("the second category without a name", FileWith(Section("categories", Category("Bug"), Category("Feature", without: "name"))), "Category 2 has no \"name\"."),
		Case("a member written as null", FileWith(Section("agents", """{ "name": "Claude", "harness": "claudeCode", "command": null, "arguments": "", "canWork": true, "canReview": true, "canMerge": false }""")), "Agent 'Claude' has no \"command\"."),
		Case("an item with a blank name, named by its place", FileWith(Section("gates", """{ "name": "  ", "command": "dotnet", "arguments": "test", "timeoutMinutes": 30 }""")), "Gate 1 has no \"enabled\"."),
		Case("an item whose name has spaces around it, named without them", FileWith(Section("categories", """{ "name": "  Bug  " }""")), "Category 'Bug' has no \"color\"."),
		Case("a sandbox section without a Dockerfile", FileWith("\"sandbox\": {}"), "The sandbox section has no \"dockerfile\"."),
		Case("a sandbox section with a null Dockerfile", FileWith("\"sandbox\": { \"dockerfile\": null }"), "The sandbox section has no \"dockerfile\"."),
	];

	public static IEnumerable<TheoryDataRow<string, string>> NullItems { get; } =
	[
		Case("a null agent", FileWith(Section("agents", "null")), "Agent 1 is empty."),
		Case("a null second gate", FileWith(Section("gates", Gate("Build"), "null")), "Gate 2 is empty."),
		Case("a null third category", FileWith(Section("categories", Category("Bug"), Category("Feature"), "null")), "Category 3 is empty."),
	];

	[Theory]
	[MemberData(nameof(MissingMembers))]
	public void Read_OfAnItemWithoutAMemberVersion1Requires_NamesTheItemAndTheMember(string text, string expected)
	{
		Assert.Equal(expected, ResultAssert.Failed(Read(text)));
	}

	[Theory]
	[MemberData(nameof(NullItems))]
	public void Read_OfANullItem_SaysWhichItemIsEmpty(string text, string expected)
	{
		Assert.Equal(expected, ResultAssert.Failed(Read(text)));
	}

	[Fact]
	public void Read_OfAnEmptyItem_ListsEveryMemberItLacks()
	{
		string error = ResultAssert.Failed(Read(FileWith(Section("agents", "{}"))));

		Assert.Equal("Agent 1 has no \"name\".\nAgent 1 has no \"harness\".\nAgent 1 has no \"command\".\nAgent 1 has no \"arguments\".\nAgent 1 has no \"canWork\".\nAgent 1 has no \"canReview\".\nAgent 1 has no \"canMerge\".", error);
	}

	[Theory]
	[InlineData("gemini")]
	[InlineData("ClaudeCode")]
	[InlineData("")]
	public void Read_OfAHarnessNameThisPyxieDoesNotKnow_NamesItAndListsTheHarnesses(string harness)
	{
		string error = ResultAssert.Failed(Read(FileWith(Section("agents", Agent("Claude", harness: $"\"{harness}\"")))));

		Assert.Equal($"Agent 'Claude' has harness \"{harness}\", which this Pyxie does not know. {Harnesses}", error);
	}

	[Theory]
	[InlineData("red")]
	[InlineData("#FFF")]
	[InlineData("#GGGGGG")]
	[InlineData("#FBE9EC00")]
	[InlineData("")]
	public void Read_OfAColorNotWrittenAsSixHexDigits_NamesItAndSaysHowAColorIsWritten(string color)
	{
		string error = ResultAssert.Failed(Read(FileWith(Section("categories", Category("Bug", color: color)))));

		Assert.Equal($"Category 'Bug' has color \"{color}\", which is not a color written #RRGGBB.", error);
	}

	[Fact]
	public void Read_OfAColorInLowerCase_ReadsTheColorWhichIsWrittenInUpperCase()
	{
		ProjectSettings settings = ResultAssert.Succeeded(Read(FileWith(Section("categories", Category("Feature", color: "#eaf3e2")))));

		CategoryInput category = Assert.Single(settings.Categories ?? []);
		Assert.Equal(CategoryColors.Green, category.Color);
		Assert.Equal("#EAF3E2", category.Color.ToString());
	}

	[Fact]
	public void Read_OfAFileWithProblemsInEverySection_ListsEveryProblemOnItsOwnLineInTheOrderOfTheSections()
	{
		string text = FileWith(
			"\"sandbox\": {}",
			Section("categories", Category("Bug", color: "red"), Category("Feature", without: "name")),
			Section("gates", Gate("Build", without: "enabled")),
			Section("agents", "null", Agent("Claude", without: "canMerge", harness: "\"claude\"")));

		string error = ResultAssert.Failed(Read(text));

		Assert.Equal(
			[
				"Agent 1 is empty.",
				"Agent 'Claude' has no \"canMerge\".",
				$"Agent 'Claude' has harness \"claude\", which this Pyxie does not know. {Harnesses}",
				"Gate 'Build' has no \"enabled\".",
				"Category 'Bug' has color \"red\", which is not a color written #RRGGBB.",
				"Category 2 has no \"name\".",
				"The sandbox section has no \"dockerfile\".",
			],
			error.Split('\n'));
	}

	private static TheoryDataRow<string, string> Case(string description, string text, string expected)
	{
		return new TheoryDataRow<string, string>(text, expected) { TestDisplayName = description };
	}
}
