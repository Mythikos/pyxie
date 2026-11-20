using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="AgentInputRules.Validate"/> checks an agent's fields as they will be stored, after trimming, and reports the first that is not valid, in the order the editor shows them: the harness, the name, the command, then the arguments. The command is optional at save, as it is in the agent editor.</summary>
public sealed class AgentInputRulesValidateTests
{
	private const string UnknownTokenMessage = "Arguments use {CARDNAME}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";

	private static readonly string s_longCommand = new('c', PyxieLimits.MaxCommandLength + 1);

	public static TheoryData<HarnessKind> Harnesses { get; } = [.. Enum.GetValues<HarnessKind>()];

	public static IEnumerable<TheoryDataRow<HarnessKind, string, string, string, string>> FirstProblems { get; } =
	[
		new((HarnessKind)99, "", s_longCommand, "{CARDNAME}", "Harness is not recognized.") { TestDisplayName = "the harness before everything else" },
		new(HarnessKind.ClaudeCode, "", s_longCommand, "{CARDNAME}", "Agent name is required.") { TestDisplayName = "the name before the command" },
		new(HarnessKind.ClaudeCode, "Claude", s_longCommand, "{CARDNAME}", "Command must be 500 characters or fewer.") { TestDisplayName = "the command before the arguments" },
		new(HarnessKind.ClaudeCode, "Claude", "claude", "{CARDNAME}", UnknownTokenMessage) { TestDisplayName = "the arguments last" },
	];

	[Theory]
	[InlineData(HarnessKind.Codex, AgentAuthentication.None)]
	[InlineData(HarnessKind.ClaudeCode, AgentAuthentication.None)]
	[InlineData(HarnessKind.DeepSeek, AgentAuthentication.None)]
	[InlineData(HarnessKind.Other, AgentAuthentication.Account)]
	[InlineData(HarnessKind.Other, AgentAuthentication.ApiKey)]
	[InlineData(HarnessKind.Codex, (AgentAuthentication)99)]
	public void Validate_RefusesAnUnsupportedAuthenticationMethod(HarnessKind harness, AgentAuthentication authentication)
	{
		AgentInput input = Agent() with { Harness = harness, Authentication = authentication };
		Assert.Equal("Choose an authentication method supported by this harness.", AgentInputRules.Validate(input));
	}

	[Theory]
	[MemberData(nameof(Harnesses))]
	public void Validate_OfAValidAgentOfEveryHarness_FindsNothing(HarnessKind harness)
	{
		Assert.Null(AgentInputRules.Validate(new AgentInput("Claude", harness, "claude", "-p --name \"{CARD_NAME}\"", CanWork: true, CanReview: true, CanMerge: true)));
	}

	[Theory]
	[InlineData(99)]
	[InlineData(-1)]
	public void Validate_OfAHarnessPyxieDoesNotHave_SaysItIsNotRecognized(int harness)
	{
		Assert.Equal("Harness is not recognized.", AgentInputRules.Validate(Agent() with { Harness = (HarnessKind)harness }));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void Validate_OfABlankName_SaysTheNameIsRequired(string name)
	{
		Assert.Equal("Agent name is required.", AgentInputRules.Validate(Agent() with { Name = name }));
	}

	[Fact]
	public void Validate_OfANameAtTheLimitAfterTrimming_FindsNothing_AndOfOneOver_SaysHowLongItMayBe()
	{
		Assert.Null(AgentInputRules.Validate(Agent() with { Name = $"  {new string('n', PyxieLimits.MaxNameLength)}  " }));
		Assert.Equal("Agent name must be 100 characters or fewer.", AgentInputRules.Validate(Agent() with { Name = new string('n', PyxieLimits.MaxNameLength + 1) }));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  \t")]
	public void Validate_OfABlankCommand_FindsNothingAsTheCommandIsOptional(string command)
	{
		Assert.Null(AgentInputRules.Validate(Agent() with { Command = command }));
	}

	[Fact]
	public void Validate_OfACommandAtTheLimitAfterTrimming_FindsNothing_AndOfOneOver_SaysHowLongItMayBe()
	{
		Assert.Null(AgentInputRules.Validate(Agent() with { Command = $"  {new string('c', PyxieLimits.MaxCommandLength)}  " }));
		Assert.Equal("Command must be 500 characters or fewer.", AgentInputRules.Validate(Agent() with { Command = s_longCommand }));
	}

	[Fact]
	public void Validate_ChecksTheArgumentsAsValidateArgumentsDoes()
	{
		Assert.Equal("Arguments must be 2000 characters or fewer.", AgentInputRules.Validate(Agent() with { Arguments = new string('a', PyxieLimits.MaxArgumentsLength + 1) }));
		Assert.Equal(UnknownTokenMessage, AgentInputRules.Validate(Agent() with { Arguments = "--name {CARDNAME}" }));
		Assert.Null(AgentInputRules.Validate(Agent() with { Arguments = "" }));
	}

	[Theory]
	[MemberData(nameof(FirstProblems))]
	public void Validate_OfSeveralProblems_ReportsTheFirstInTheEditorsOrder(HarnessKind harness, string name, string command, string arguments, string expected)
	{
		Assert.Equal(expected, AgentInputRules.Validate(new AgentInput(name, harness, command, arguments, CanWork: true, CanReview: false)));
	}

	private static AgentInput Agent()
	{
		return new AgentInput("Claude", HarnessKind.ClaudeCode, "claude", "-p", CanWork: true, CanReview: true);
	}
}
