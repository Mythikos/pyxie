using System.Text.Json;

using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessResultFile.Parse"/>: what an agent's <c>.pyxie/result.json</c> may say, and how a file that says something else is refused.</summary>
public sealed class HarnessResultFileParseTests
{
	[Theory]
	[InlineData(RoundKind.Work, "completed", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Work, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Review, "approved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Review, "rejected", RoundResult.Failed)]
	[InlineData(RoundKind.Review, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Review, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Resolve, "resolved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Resolve, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Resolve, "blocked", RoundResult.Blocked)]
	public void Parse_ReadsEachOutcomeWordOfTheRoundKind(RoundKind kind, string word, RoundResult expected)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(JsonSerializer.Serialize(new { outcome = word }), kind);

		Assert.Null(problem);
		Assert.NotNull(verdict);
		Assert.Equal(expected, verdict.Result);
	}

	[Theory]
	[InlineData(RoundKind.Work, "COMPLETED")]
	[InlineData(RoundKind.Work, "Completed")]
	[InlineData(RoundKind.Work, "  completed  ")]
	[InlineData(RoundKind.Work, "\tCompleted\n")]
	[InlineData(RoundKind.Review, " ApProved ")]
	[InlineData(RoundKind.Resolve, " Resolved ")]
	public void Parse_IgnoresCaseAndSurroundingSpacesInTheOutcomeWord(RoundKind kind, string word)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(JsonSerializer.Serialize(new { outcome = word }), kind);

		Assert.Null(problem);
		Assert.Equal(RoundResult.Succeeded, verdict?.Result);
	}

	[Theory]
	[InlineData(RoundKind.Work, "approved")]
	[InlineData(RoundKind.Work, "rejected")]
	[InlineData(RoundKind.Review, "completed")]
	[InlineData(RoundKind.Work, "done")]
	[InlineData(RoundKind.Review, "approve")]
	[InlineData(RoundKind.Work, "complete d")]
	[InlineData(RoundKind.Work, "")]
	[InlineData(RoundKind.Work, "resolved")]
	[InlineData(RoundKind.Resolve, "completed")]
	[InlineData(RoundKind.Resolve, "approved")]
	public void Parse_RefusesAnOutcomeWordTheRoundKindDoesNotUse_AndListsTheWordsItDoes(RoundKind kind, string word)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(JsonSerializer.Serialize(new { outcome = word, note = "n" }), kind);

		Assert.Null(verdict);
		Assert.NotNull(problem);
		Assert.Contains(PipelineContract.ResultFile, problem, StringComparison.Ordinal);
		Assert.Contains($"outcome \"{word}\"", problem, StringComparison.Ordinal);
		Assert.Contains(string.Join(", ", PipelineContract.OutcomeWords(kind)), problem, StringComparison.Ordinal);
		Assert.Contains(kind.DisplayName(), problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("{}", "")]
	[InlineData("""{"outcome": null}""", "")]
	[InlineData("""{"note": "no outcome here"}""", "")]
	[InlineData("""{"outcome": 5}""", "5")]
	[InlineData("""{"outcome": true}""", "true")]
	[InlineData("""{"outcome": ["completed"]}""", """["completed"]""")]
	[InlineData("""{"outcome": {"word": "completed"}}""", """{"word": "completed"}""")]
	public void Parse_RefusesAnOutcomeThatIsMissingOrNotAWord_AndQuotesWhatItFound(string json, string reportedOutcome)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(json, RoundKind.Work);

		Assert.Null(verdict);
		Assert.NotNull(problem);
		Assert.Contains($"outcome \"{reportedOutcome}\"", problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("[]")]
	[InlineData("""[{"outcome": "completed"}]""")]
	[InlineData("""
		"completed"
		""")]
	[InlineData("42")]
	[InlineData("true")]
	[InlineData("null")]
	public void Parse_RefusesJsonThatIsNotAnObject(string json)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(json, RoundKind.Work);

		Assert.Null(verdict);
		Assert.NotNull(problem);
		Assert.Contains("is not a JSON object", problem, StringComparison.Ordinal);
		Assert.Contains(PipelineContract.ResultFile, problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("{")]
	[InlineData("not json")]
	[InlineData("""{"outcome": }""")]
	[InlineData("""{"outcome": "completed" """)]
	[InlineData("{'outcome': 'completed'}")]
	[InlineData("""{"outcome": "completed"} trailing text""")]
	public void Parse_RefusesTextThatIsNotJson(string text)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(text, RoundKind.Work);

		Assert.Null(verdict);
		Assert.NotNull(problem);
		Assert.Contains("is not valid JSON", problem, StringComparison.Ordinal);
		Assert.Contains(PipelineContract.ResultFile, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_AllowsCommentsAndTrailingCommas()
	{
		const string Text = """
			{
			  // what happened
			  "outcome": "completed", /* inline comment */
			  "note": "done",
			  "report": "all of it",
			}
			""";

		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse(Text, RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "done", "all of it"), verdict);
	}

	[Fact]
	public void Parse_MatchesPropertyNamesWithoutRegardToCase()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse("""{"OUTCOME": "completed", "Note": "n", "rEpOrT": "r"}""", RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "n", "r"), verdict);
	}

	[Fact]
	public void Parse_IgnoresPropertiesItDoesNotKnow()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse("""{"outcome": "approved", "tokens": 12, "extra": {"a": [1, 2]}}""", RoundKind.Review);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, null, null), verdict);
	}

	[Fact]
	public void Parse_WithoutANoteOrReport_LeavesThemNull()
	{
		(RoundVerdict? verdict, _) = HarnessResultFile.Parse("""{"outcome": "completed"}""", RoundKind.Work);

		Assert.NotNull(verdict);
		Assert.Null(verdict.Note);
		Assert.Null(verdict.Report);
	}

	[Theory]
	[InlineData("12", "12")]
	[InlineData("1.50", "1.50")]
	[InlineData("true", "true")]
	[InlineData("false", "false")]
	[InlineData("""{"a": 1}""", """{"a": 1}""")]
	[InlineData("[1,  2]", "[1,  2]")]
	public void Parse_KeepsANoteOrReportThatIsNotAStringAsItsJsonText(string value, string expected)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse($$"""{"outcome": "completed", "note": {{value}} , "report": {{value}} }""", RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, expected, expected), verdict);
	}

	[Fact]
	public void Parse_ANullNoteOrReport_IsNull()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse("""{"outcome": "completed", "note": null, "report": null}""", RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, null, null), verdict);
	}

	[Fact]
	public void Parse_DecodesEscapesInTheNoteAndReportAndKeepsThemUntrimmed()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Parse("""{"outcome": "errored", "note": "  two\nlines ✓ and \"quotes\"  ", "report": ""}""", RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Errored, "  two\nlines ✓ and \"quotes\"  ", string.Empty), verdict);
	}
}
