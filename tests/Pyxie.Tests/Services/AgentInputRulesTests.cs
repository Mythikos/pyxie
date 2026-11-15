using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="AgentInputRules"/>: an agent's arguments are optional, trimmed, within the length limit, and carry only tokens Pyxie fills in; the limit is checked before the tokens.</summary>
public sealed class AgentInputRulesTests
{
	private const string UnknownTokenMessage = "Arguments use {CARDNAME}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";
	private const string LengthMessage = "Arguments must be 2000 characters or fewer.";

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateArguments_AcceptsMissingAndBlankArguments(string? arguments)
	{
		Assert.Null(AgentInputRules.ValidateArguments(arguments));
	}

	[Theory]
	[InlineData("-p --dangerously-skip-permissions")]
	[InlineData("exec --yolo --model gpt-6-astra -c model_reasoning_effort=high")]
	[InlineData("-p --dangerously-skip-permissions --name \"{CARD_NAME}\"")]
	[InlineData("--name=card-{CARD_ID} --branch {BRANCH} --stage {STAGE} --attempt {ATTEMPT}")]
	[InlineData("  --name \"{CARD_NAME}\"  ")]
	[InlineData("-c \"echo ${HOME}\" {\"outcome\":\"completed\"} {lower}")]
	public void ValidateArguments_AcceptsOrdinaryArgumentsAndKnownTokens(string arguments)
	{
		Assert.Null(AgentInputRules.ValidateArguments(arguments));
	}

	[Fact]
	public void ValidateArguments_AcceptsArgumentsAtTheLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(AgentInputRules.ValidateArguments($"  {new string('a', PyxieLimits.MaxArgumentsLength)}  "));
		Assert.Equal(LengthMessage, AgentInputRules.ValidateArguments(new string('a', PyxieLimits.MaxArgumentsLength + 1)));
	}

	[Fact]
	public void ValidateArguments_ChecksTheLengthBeforeTheTokens()
	{
		string tooLongWithAnUnknownToken = $"--name {{CARDNAME}} {new string('a', PyxieLimits.MaxArgumentsLength)}";

		Assert.Equal(LengthMessage, AgentInputRules.ValidateArguments(tooLongWithAnUnknownToken));
	}

	[Fact]
	public void ValidateArguments_ChecksTheTokensOfArgumentsThatFitTheLimit()
	{
		string fitsWithAnUnknownToken = $"--name {{CARDNAME}}{new string('a', PyxieLimits.MaxArgumentsLength - "--name {CARDNAME}".Length)}";
		Assert.Equal(PyxieLimits.MaxArgumentsLength, fitsWithAnUnknownToken.Length);

		Assert.Equal(UnknownTokenMessage, AgentInputRules.ValidateArguments(fitsWithAnUnknownToken));
	}

	[Theory]
	[InlineData("--name {CARDNAME}")]
	[InlineData("-p --name \"{CARDNAME}\"")]
	[InlineData("  --name=card-{CARDNAME}  ")]
	public void ValidateArguments_RejectsANameInCapitalsThatIsNotATokenAndListsTheTokens(string arguments)
	{
		Assert.Equal(UnknownTokenMessage, AgentInputRules.ValidateArguments(arguments));
	}

	[Fact]
	public void ValidateArguments_NamesSeveralUnknownTokensOnceEachInTheOrderTheyAreUsed()
	{
		string? message = AgentInputRules.ValidateArguments("--a {ZED} --b {ALPHA} --c {ZED}");

		Assert.Equal("Arguments use {ZED} and {ALPHA}, which are not tokens Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.", message);
	}
}
