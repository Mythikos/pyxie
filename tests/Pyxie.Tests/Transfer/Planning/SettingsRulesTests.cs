using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Planning;

using Xunit;

namespace Pyxie.Tests.Transfer.Planning;

/// <summary><see cref="SettingsRules.Check"/> applies the editors' rules to a file's normalized settings, section by section, in the editors' own words: each item's first problem, prefixed with the item's name or, for an item with no name, its place in its list; names that repeat ignoring case; more than one merge agent; and a Dockerfile that does not start from Pyxie's base image. A problem in one section leaves every other section importable.</summary>
public sealed class SettingsRulesTests
{
	private const string Tokens = "The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";

	private static readonly string s_longName = new('n', PyxieLimits.MaxNameLength + 1);
	private static readonly string s_longCommand = new('c', PyxieLimits.MaxCommandLength + 1);
	private static readonly string s_longArguments = new('a', PyxieLimits.MaxArgumentsLength + 1);

	public static IEnumerable<TheoryDataRow<string, HarnessKind, string, string, string>> AgentProblems { get; } =
	[
		new("Claude", (HarnessKind)99, "claude", "-p", "Agent 'Claude': Harness is not recognized.") { TestDisplayName = "a harness Pyxie does not have" },
		new("", HarnessKind.ClaudeCode, "claude", "-p", "Agent 2: Agent name is required.") { TestDisplayName = "no name, named by its place" },
		new("   ", HarnessKind.ClaudeCode, "claude", "-p", "Agent 2: Agent name is required.") { TestDisplayName = "a blank name, named by its place" },
		new(s_longName, HarnessKind.ClaudeCode, "claude", "-p", $"Agent '{s_longName}': Agent name must be 100 characters or fewer.") { TestDisplayName = "a name over the limit" },
		new("Claude", HarnessKind.ClaudeCode, s_longCommand, "-p", "Agent 'Claude': Command must be 500 characters or fewer.") { TestDisplayName = "a command over the limit" },
		new("Claude", HarnessKind.ClaudeCode, "claude", s_longArguments, "Agent 'Claude': Arguments must be 2000 characters or fewer.") { TestDisplayName = "arguments over the limit" },
		new("Claude", HarnessKind.ClaudeCode, "claude", "-p --name {CARDNAME}", $"Agent 'Claude': Arguments use {{CARDNAME}}, which is not a token Pyxie fills in. {Tokens}") { TestDisplayName = "arguments with a name that is not a token" },
		new("", HarnessKind.ClaudeCode, s_longCommand, "{CARDNAME}", "Agent 2: Agent name is required.") { TestDisplayName = "several problems, of which the first is reported" },
	];

	public static IEnumerable<TheoryDataRow<string, string, string, int, string>> GateProblems { get; } =
	[
		new("", "dotnet", "test", 30, "Gate 2: Gate name is required.") { TestDisplayName = "no name, named by its place" },
		new(s_longName, "dotnet", "test", 30, $"Gate '{s_longName}': Gate name must be 100 characters or fewer.") { TestDisplayName = "a name over the limit" },
		new("Tests", "", "test", 30, "Gate 'Tests': Command is required.") { TestDisplayName = "no command" },
		new("Tests", "  ", "test", 30, "Gate 'Tests': Command is required.") { TestDisplayName = "a blank command" },
		new("Tests", s_longCommand, "test", 30, "Gate 'Tests': Command must be 500 characters or fewer.") { TestDisplayName = "a command over the limit" },
		new("Tests", "dotnet", s_longArguments, 30, "Gate 'Tests': Arguments must be 2000 characters or fewer.") { TestDisplayName = "arguments over the limit" },
		new("Tests", "dotnet", "test", 0, "Gate 'Tests': Timeout must be between 1 and 240 minutes.") { TestDisplayName = "no timeout" },
		new("Tests", "dotnet", "test", PyxieLimits.MaxGateTimeoutMinutes + 1, "Gate 'Tests': Timeout must be between 1 and 240 minutes.") { TestDisplayName = "a timeout over the limit" },
	];

	public static IEnumerable<TheoryDataRow<string, string>> CategoryProblems { get; } =
	[
		new("", "Category 3: Category name is required.") { TestDisplayName = "no name, named by its place" },
		new(" \t ", "Category 3: Category name is required.") { TestDisplayName = "a blank name, named by its place" },
		new(s_longName, $"Category '{s_longName}': Category name must be 100 characters or fewer.") { TestDisplayName = "a name over the limit" },
	];

	public static IEnumerable<TheoryDataRow<string, string>> DockerfileProblems { get; } =
	[
		new("FROM node:22\nRUN npm ci\n", "The Dockerfile must start FROM ${PYXIE_BASE}, Pyxie's base image.") { TestDisplayName = "not from the base image" },
		new(" \r\n\t", "Enter the project's Dockerfile.") { TestDisplayName = "blank" },
		new("FROM ${PYXIE_BASE}\n" + new string('#', PyxieLimits.MaxDockerfileLength), $"The Dockerfile must be {PyxieLimits.MaxDockerfileLength} characters or fewer.") { TestDisplayName = "over the limit" },
	];

	public static TheoryData<TransferSection> Sections { get; } = [.. Enum.GetValues<TransferSection>()];

	[Theory]
	[MemberData(nameof(AgentProblems))]
	public void Check_OfAnAgentThatBreaksARule_SaysWhichAgentAndWhy(string name, HarnessKind harness, string command, string arguments, string expected)
	{
		ProjectSettings settings = new([Agent("Codex"), new AgentInput(name, harness, command, arguments, CanWork: true, CanReview: false)], null, null, null);

		Assert.Equal([expected], Check(settings, TransferSection.Agents));
	}

	[Theory]
	[MemberData(nameof(GateProblems))]
	public void Check_OfAGateThatBreaksARule_SaysWhichGateAndWhy(string name, string command, string arguments, int timeoutMinutes, string expected)
	{
		ProjectSettings settings = new(null, [Gate("Build"), new GateInput(name, command, arguments, timeoutMinutes, IsEnabled: true)], null, null);

		Assert.Equal([expected], Check(settings, TransferSection.Gates));
	}

	[Theory]
	[MemberData(nameof(CategoryProblems))]
	public void Check_OfACategoryThatBreaksARule_SaysWhichCategoryAndWhy(string name, string expected)
	{
		ProjectSettings settings = new(null, null, [Category("Bug"), Category("Feature"), Category(name)], null);

		Assert.Equal([expected], Check(settings, TransferSection.Categories));
	}

	[Theory]
	[MemberData(nameof(DockerfileProblems))]
	public void Check_OfADockerfileThatBreaksARule_SaysWhyInTheSandboxEditorsWords(string dockerfile, string expected)
	{
		ProjectSettings settings = new(null, null, null, new SandboxSettingsInput(dockerfile));

		Assert.Equal([expected], Check(settings, TransferSection.Sandbox));
	}

	[Theory]
	[InlineData(TransferSection.Agents, "Claude|claude", new[] { "The file has more than one agent named 'Claude'." })]
	[InlineData(TransferSection.Gates, "Build|BUILD|build", new[] { "The file has more than one gate named 'Build'." })]
	[InlineData(TransferSection.Categories, "Bug| bug ", new[] { "The file has more than one category named 'Bug'." })]
	[InlineData(TransferSection.Agents, "Claude|Codex|codex|CLAUDE", new[] { "The file has more than one agent named 'Claude'.", "The file has more than one agent named 'Codex'." })]
	[InlineData(TransferSection.Gates, "Build|Tests|Lint", new string[0])]
	[InlineData(TransferSection.Categories, "| ", new[] { "Category 1: Category name is required.", "Category 2: Category name is required." })]
	public void Check_OfNamesThatRepeatIgnoringCase_NamesEachRepeatedNameOnce(TransferSection section, string names, string[] expected)
	{
		string[] written = names.Split('|');
		ProjectSettings settings = section switch
		{
			TransferSection.Agents => new ProjectSettings([.. written.Select(Agent)], null, null, null),
			TransferSection.Gates => new ProjectSettings(null, [.. written.Select(Gate)], null, null),
			_ => new ProjectSettings(null, null, [.. written.Select(Category)], null),
		};

		Assert.Equal(expected, Check(settings, section));
	}

	[Fact]
	public void Check_OfTwoMergeAgents_NamesBothAndSaysOnlyOneCanResolveConflicts()
	{
		ProjectSettings settings = new([Agent("  Claude  ") with { CanMerge = true }, Agent("Codex"), Agent("DeepSeek") with { CanMerge = true }], null, null, null);

		Assert.Equal(["The file makes 'Claude' and 'DeepSeek' merge agents. Only one agent can resolve merge conflicts."], Check(settings, TransferSection.Agents));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void Check_OfOneMergeAgentOrNone_FindsNothing(int mergeAgents)
	{
		ProjectSettings settings = new([Agent("Claude") with { CanMerge = mergeAgents > 0 }, Agent("Codex")], null, null, null);

		Assert.Empty(Check(settings, TransferSection.Agents));
	}

	[Fact]
	public void Check_OfASectionWithSeveralProblems_ListsTheItemsProblemsThenTheRepeatedNamesThenTheMergeAgents()
	{
		ProjectSettings settings = new(
			[
				Agent(""),
				new AgentInput("Claude", (HarnessKind)99, "claude", "", CanWork: true, CanReview: true),
				Agent("claude") with { CanMerge = true },
				Agent("Codex") with { CanMerge = true },
				Agent("codex"),
			],
			null,
			null,
			null);

		Assert.Equal(
			[
				"Agent 1: Agent name is required.",
				"Agent 'Claude': Harness is not recognized.",
				"The file has more than one agent named 'Claude'.",
				"The file has more than one agent named 'Codex'.",
				"The file makes 'claude' and 'Codex' merge agents. Only one agent can resolve merge conflicts.",
			],
			Check(settings, TransferSection.Agents));
	}

	[Fact]
	public void Check_OfValidSettings_FindsNothingInAnySection()
	{
		ProjectSettings settings = Valid();

		Assert.All(Enum.GetValues<TransferSection>(), section => Assert.True(Check(settings, section).Count == 0, $"The {section} section of valid settings has problems: {string.Join(" ", Check(settings, section))}"));
	}

	[Theory]
	[MemberData(nameof(Sections))]
	public void Check_OfAProblemInOneSection_LeavesEveryOtherSectionImportable(TransferSection broken)
	{
		ProjectSettings valid = Valid();
		ProjectSettings settings = broken switch
		{
			TransferSection.Agents => valid with { Agents = [Agent("Claude"), Agent("CLAUDE")] },
			TransferSection.Gates => valid with { Gates = [Gate("Build") with { TimeoutMinutes = 0 }] },
			TransferSection.Categories => valid with { Categories = [Category("")] },
			_ => valid with { Sandbox = new SandboxSettingsInput("FROM node:22\n") },
		};

		Assert.True(Check(settings, broken).Count > 0, $"The broken {broken} section must not be importable.");
		Assert.All(Enum.GetValues<TransferSection>().Where(section => section != broken), section => Assert.True(Check(settings, section).Count == 0, $"A problem in {broken} must not stop {section}, but it has: {string.Join(" ", Check(settings, section))}"));
	}

	private static IReadOnlyList<string> Check(ProjectSettings settings, TransferSection section)
	{
		return SettingsRules.Check(SettingsRules.Normalize(settings), section);
	}

	private static ProjectSettings Valid()
	{
		return new ProjectSettings([Agent("Claude") with { CanMerge = true }, Agent("Codex")], [Gate("Build"), Gate("Tests")], [Category("Bug"), Category("Feature")], new SandboxSettingsInput(SandboxDefaults.Dockerfile));
	}

	private static AgentInput Agent(string name)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", "-p --name \"{CARD_NAME}\"", CanWork: true, CanReview: true);
	}

	private static GateInput Gate(string name)
	{
		return new GateInput(name, "dotnet", "test", PyxieLimits.DefaultGateTimeoutMinutes, IsEnabled: true);
	}

	private static CategoryInput Category(string name)
	{
		return new CategoryInput(name, CategoryColors.Neutral);
	}
}
