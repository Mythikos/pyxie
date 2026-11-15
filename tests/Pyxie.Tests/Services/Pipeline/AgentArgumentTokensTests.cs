using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary><see cref="AgentArgumentTokens"/>: each token takes the value of its round, a value is filled in once and stays inside its argument, a shell's <c>${NAME}</c> and braces that hold no capitalised name are left alone, and any other capitalised name in braces is refused with one message that lists what the tokens are.</summary>
public sealed class AgentArgumentTokensTests
{
	private const string TokenList = "The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";

	private static PipelineWork Work(RoundKind kind = RoundKind.Review, string cardName = "Fix the login page")
	{
		return new PipelineWork(CardId: 12, cardName, RoundId: 7, Sequence: 2, kind, Attempt: 3, Agent: null, Briefing: null);
	}

	private static IReadOnlyList<string> Expand(IReadOnlyList<string> arguments, PipelineWork? work = null)
	{
		string? problem = AgentArgumentTokens.TryExpand(arguments, work ?? Work(), out IReadOnlyList<string> expanded);
		Assert.Null(problem);

		return expanded;
	}

	private static string Refuse(IReadOnlyList<string> arguments)
	{
		string? problem = AgentArgumentTokens.TryExpand(arguments, Work(), out IReadOnlyList<string> expanded);
		Assert.NotNull(problem);
		Assert.Empty(expanded);

		return problem;
	}

	[Fact]
	public void All_ListsTheTokensInTheOrderMessagesNameThem()
	{
		Assert.Equal(["{CARD_ID}", "{CARD_NAME}", "{BRANCH}", "{STAGE}", "{ATTEMPT}"], AgentArgumentTokens.All);
	}

	[Theory]
	[InlineData("{CARD_ID}", "12")]
	[InlineData("{CARD_NAME}", "Fix the login page")]
	[InlineData("{BRANCH}", "pyxie/card-12")]
	[InlineData("{STAGE}", "Review")]
	[InlineData("{ATTEMPT}", "3")]
	public void TryExpand_FillsInEachTokenWithItsValueOfTheRound(string token, string expectedValue)
	{
		IReadOnlyList<string> expanded = Expand([token]);

		Assert.Equal([expectedValue], expanded);
	}

	[Theory]
	[InlineData(RoundKind.Work, "Work")]
	[InlineData(RoundKind.Review, "Review")]
	[InlineData(RoundKind.Resolve, "Resolve")]
	public void TryExpand_FillsInTheStageAsTheBriefingNamesIt(RoundKind kind, string expectedStage)
	{
		IReadOnlyList<string> expanded = Expand([AgentArgumentTokens.Stage], Work(kind));

		Assert.Equal([expectedStage], expanded);
	}

	[Theory]
	[InlineData("--name=card-{CARD_ID}", "--name=card-12")]
	[InlineData("#{CARD_ID} {CARD_NAME} ({STAGE})", "#12 Fix the login page (Review)")]
	[InlineData("{BRANCH}@{ATTEMPT}", "pyxie/card-12@3")]
	[InlineData("{CARD_ID}{ATTEMPT}", "123")]
	public void TryExpand_FillsInATokenInsideALongerArgument(string argument, string expected)
	{
		IReadOnlyList<string> expanded = Expand([argument]);

		Assert.Equal([expected], expanded);
	}

	[Fact]
	public void TryExpand_FillsInEachArgumentAndKeepsTheirNumberAndOrder()
	{
		IReadOnlyList<string> expanded = Expand(["-p", "--name", "{CARD_NAME}", "--session", "{BRANCH}", "--dangerously-skip-permissions"]);

		Assert.Equal(["-p", "--name", "Fix the login page", "--session", "pyxie/card-12", "--dangerously-skip-permissions"], expanded);
	}

	[Fact]
	public void TryExpand_WithNoArguments_ReturnsNone()
	{
		Assert.Empty(Expand([]));
	}

	[Fact]
	public void TryExpand_KeepsAValueWithSpacesAndQuotesAsExactlyOneArgument()
	{
		const string CardName = "Say \"hi\" to  everyone";

		IReadOnlyList<string> expanded = Expand(["--name", "{CARD_NAME}", "-p"], Work(cardName: CardName));

		Assert.Equal(["--name", CardName, "-p"], expanded);
	}

	[Fact]
	public void TryExpand_FillsInOnce_SoAValueThatLooksLikeTokensStaysLiteral()
	{
		IReadOnlyList<string> expanded = Expand(["--name", "{CARD_NAME}"], Work(cardName: "{BRANCH} and {CARD_ID}"));

		Assert.Equal(["--name", "{BRANCH} and {CARD_ID}"], expanded);
	}

	[Fact]
	public void TryExpand_DoesNotRefuseACardNameThatLooksLikeAnUnknownToken()
	{
		IReadOnlyList<string> expanded = Expand(["--name=card:{CARD_NAME}"], Work(cardName: "{NOPE}"));

		Assert.Equal(["--name=card:{NOPE}"], expanded);
	}

	[Theory]
	[InlineData("${HOME}")]
	[InlineData("${CARD_NAME}")]
	[InlineData("{\"outcome\":\"completed\"}")]
	[InlineData("{}")]
	[InlineData("{lower}")]
	[InlineData("{ CARD_NAME }")]
	[InlineData("{Card_Name}")]
	[InlineData("{1ABC}")]
	[InlineData("-p")]
	[InlineData("--dangerously-skip-permissions")]
	[InlineData("exec")]
	public void TryExpand_LeavesWhatIsNotATokenAsItIsAndDoesNotRefuseIt(string argument)
	{
		IReadOnlyList<string> expanded = Expand([argument]);

		Assert.Equal([argument], expanded);
		Assert.Null(AgentArgumentTokens.Validate(argument));
	}

	[Fact]
	public void TryExpand_LeavesAShellsVariableBesideATokenAlone()
	{
		IReadOnlyList<string> expanded = Expand(["${CARD_NAME}-{CARD_ID}-${HOME}"]);

		Assert.Equal(["${CARD_NAME}-12-${HOME}"], expanded);
	}

	[Fact]
	public void TryExpand_RefusesANameInCapitalsThatIsNotATokenAndSaysWhatTheTokensAre()
	{
		string problem = Refuse(["--name", "{CARDNAME}"]);

		Assert.Equal($"Arguments use {{CARDNAME}}, which is not a token Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_RefusesATokenInsideALongerArgument()
	{
		string problem = Refuse(["--name=card-{NOPE}"]);

		Assert.Equal($"Arguments use {{NOPE}}, which is not a token Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_NamesTwoUnknownTokensWithAnd()
	{
		string problem = Refuse(["{FIRST}", "{SECOND}"]);

		Assert.Equal($"Arguments use {{FIRST}} and {{SECOND}}, which are not tokens Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_NamesThreeUnknownTokensWithAnOxfordComma()
	{
		string problem = Refuse(["{A}", "{B}", "{C}"]);

		Assert.Equal($"Arguments use {{A}}, {{B}}, and {{C}}, which are not tokens Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_NamesFourUnknownTokensWithCommasAndAnOxfordComma()
	{
		string problem = Refuse(["{A} {B}", "{C}", "{D}"]);

		Assert.Equal($"Arguments use {{A}}, {{B}}, {{C}}, and {{D}}, which are not tokens Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_NamesARepeatedUnknownTokenOnce()
	{
		string problem = Refuse(["{NOPE}", "x{NOPE}y", "{NOPE}"]);

		Assert.Equal($"Arguments use {{NOPE}}, which is not a token Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_NamesUnknownTokensInTheOrderTheyAreFirstUsed()
	{
		string problem = Refuse(["{ZED}", "{ALPHA} {ZED}", "{MID}", "{ALPHA}"]);

		Assert.Equal($"Arguments use {{ZED}}, {{ALPHA}}, and {{MID}}, which are not tokens Pyxie fills in. {TokenList}", problem);
	}

	[Fact]
	public void TryExpand_RefusesAllTheArgumentsWhenOneUnknownTokenIsMixedWithKnownOnes()
	{
		string problem = Refuse(["--name", "{CARD_NAME}", "{NOPE}", "{BRANCH}"]);

		Assert.Equal($"Arguments use {{NOPE}}, which is not a token Pyxie fills in. {TokenList}", problem);
	}

	[Theory]
	[InlineData("")]
	[InlineData("-p --dangerously-skip-permissions")]
	[InlineData("-p --name \"{CARD_NAME}\" --branch={BRANCH} {CARD_ID} {STAGE} {ATTEMPT}")]
	[InlineData("-p --name \"${HOME}\" ${CARD_NAME}")]
	[InlineData("--json {\"outcome\":\"completed\"} {} {lower}")]
	public void Validate_AcceptsArgumentsWithNothingButTokens(string arguments)
	{
		Assert.Null(AgentArgumentTokens.Validate(arguments));
	}

	[Fact]
	public void Validate_RefusesAnUnknownTokenWithTheMessageOfTryExpand()
	{
		string problem = Refuse(["--name", "{CARDNAME}"]);

		Assert.Equal(problem, AgentArgumentTokens.Validate("--name \"{CARDNAME}\""));
	}

	[Fact]
	public void Validate_NamesEveryUnknownTokenOfTheStringOnceInTheOrderItIsFirstUsed()
	{
		string? message = AgentArgumentTokens.Validate("-p --name {ZED} --alias \"{ALPHA} and {ZED}\" --stage {STAGE}");

		Assert.Equal($"Arguments use {{ZED}} and {{ALPHA}}, which are not tokens Pyxie fills in. {TokenList}", message);
	}

	[Fact]
	public void Validate_DoesNotRefuseAShellsVariableOfAnUnknownName()
	{
		Assert.Null(AgentArgumentTokens.Validate("-c \"echo ${NOPE} ${CARD_NAME}\""));
	}
}
