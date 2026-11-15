using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Harnesses;

/// <summary><see cref="HarnessResultFile.Read"/>: the result file's text as the sandbox hands it over, which is missing when the agent wrote none, is read one character past the limit so an oversized file shows, and is otherwise checked by <see cref="HarnessResultFile.Parse"/>.</summary>
public sealed class HarnessResultFileReadTests
{
	private const int FourMegabytes = 4 * 1024 * 1024;

	[Fact]
	public void Read_NoText_SaysTheAgentDidNotWriteTheFile()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read(text: null, RoundKind.Work);

		Assert.Null(verdict);
		Assert.Equal($"The agent did not write {PipelineContract.ResultFile}.", problem);
	}

	[Fact]
	public void Read_AValidText_ReturnsItsVerdict()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read("""{"outcome": "rejected", "note": "needs a test", "report": "details"}""", RoundKind.Review);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Failed, "needs a test", "details"), verdict);
	}

	[Theory]
	[InlineData("")]
	[InlineData("I finished the work.")]
	public void Read_AnInvalidText_ReportsWhyItIsInvalid(string text)
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read(text, RoundKind.Work);

		Assert.Null(verdict);
		Assert.Equal(HarnessResultFile.Parse(text, RoundKind.Work).Problem, problem);
		Assert.Contains("is not valid JSON", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_TheRoundKindDecidesWhichWordsAreValid()
	{
		const string Text = """{"outcome": "approved"}""";

		(RoundVerdict? asReview, string? reviewProblem) = HarnessResultFile.Read(Text, RoundKind.Review);
		(RoundVerdict? asWork, string? workProblem) = HarnessResultFile.Read(Text, RoundKind.Work);

		Assert.Null(reviewProblem);
		Assert.Equal(RoundResult.Succeeded, asReview?.Result);
		Assert.Null(asWork);
		Assert.Contains("outcome \"approved\"", workProblem, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_ATextOfExactlyFourMegabytes_IsRead()
	{
		string text = PaddedTo(FourMegabytes);

		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read(text, RoundKind.Work);

		Assert.Equal(HarnessResultFile.MaxLength, text.Length);
		Assert.Null(problem);
		Assert.Equal(RoundResult.Succeeded, verdict?.Result);
	}

	[Fact]
	public void Read_ATextOverFourMegabytes_IsRefusedEvenWhenItIsAValidResult()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read(PaddedTo(FourMegabytes + 1), RoundKind.Work);

		Assert.Null(verdict);
		Assert.Equal($"{PipelineContract.ResultFile} is larger than 4 MB.", problem);
	}

	// An agent's tools may write the file with a UTF-8 byte order mark, and the Docker sandbox reads the file's bytes with head, so the mark arrives as the text's first character.
	[Fact]
	public void Read_ATextThatStartsWithAByteOrderMark_IsRead()
	{
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read("﻿{\"outcome\": \"completed\", \"note\": \"café ✓\"}", RoundKind.Work);

		Assert.Null(problem);
		Assert.Equal(new RoundVerdict(RoundResult.Succeeded, "café ✓", null), verdict);
	}

	// A valid result followed by whitespace, which JSON allows, so a text of any length can be a valid one.
	private static string PaddedTo(int length)
	{
		const string Result = """{"outcome": "completed"}""";

		return Result + new string(' ', length - Result.Length);
	}
}
