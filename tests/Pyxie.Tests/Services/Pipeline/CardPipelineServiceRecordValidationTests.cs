using System.Globalization;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Services.Rules;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Results the pipeline may not report are refused with a message and change nothing; the round stays open so the worker can report again.</summary>
public sealed class CardPipelineServiceRecordValidationTests
{
	private static async Task AssertNothingChangedAndRoundStillOpenAsync(PipelineRig rig, WorkCard card, PipelineWork running, int expectedVersion)
	{
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(expectedVersion, stored.Version);
		Assert.Equal(PipelineRules.LaneOf(running.Kind), stored.Lane);
		CardRound last = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[^1];
		Assert.Equal(running.RoundId, last.Id);
		Assert.Null(last.Outcome);
		Assert.Null(last.EndedUtc);
		Assert.Null(last.Note);
		Assert.Null(last.Report);
	}

	// A rig with an enabled gate, so cards get a gate round; every card here has the default agent as its reviewer, so cards get a review round.
	private static async Task<PipelineRig> ArrangeAsync()
	{
		PipelineRig rig = new();
		await rig.AddGateAsync();

		return rig;
	}

	private static Task<ServiceResult<PipelineWork?>> RecordAsync(PipelineRig rig, PipelineWork work, RoundResult result, string? note, string? report = null)
	{
		return rig.Pipeline.RecordResultAsync(work.RoundId, result, new RoundReport(note, report), TestContext.Current.CancellationToken);
	}

	[Theory]
	[InlineData(RoundKind.Gate, RoundResult.Failed, null)]
	[InlineData(RoundKind.Gate, RoundResult.Failed, "")]
	[InlineData(RoundKind.Gate, RoundResult.Failed, "   ")]
	[InlineData(RoundKind.Review, RoundResult.Failed, null)]
	[InlineData(RoundKind.Review, RoundResult.Failed, " \t ")]
	[InlineData(RoundKind.Work, RoundResult.Errored, null)]
	[InlineData(RoundKind.Work, RoundResult.Errored, "")]
	[InlineData(RoundKind.Gate, RoundResult.Errored, null)]
	[InlineData(RoundKind.Review, RoundResult.Errored, "  ")]
	[InlineData(RoundKind.Merge, RoundResult.Failed, null)]
	[InlineData(RoundKind.Merge, RoundResult.Failed, " ")]
	[InlineData(RoundKind.Merge, RoundResult.Errored, null)]
	[InlineData(RoundKind.Work, RoundResult.Blocked, null)]
	[InlineData(RoundKind.Gate, RoundResult.Blocked, "")]
	[InlineData(RoundKind.Review, RoundResult.Blocked, null)]
	[InlineData(RoundKind.Merge, RoundResult.Blocked, "  ")]
	public async Task RecordResultAsync_RefusesAFailedRejectedConflictedErroredOrBlockedRoundWithoutANote(RoundKind kind, RoundResult result, string? note)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, running, result, note, report: "Some output"));

		Assert.Contains("note is required", message, StringComparison.OrdinalIgnoreCase);
		await AssertNothingChangedAndRoundStillOpenAsync(rig, card, running, version);
		await rig.RecordAsync(running, result, "Now with a reason");
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task RecordResultAsync_DoesNotRequireANoteForASuccess(RoundKind kind)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);

		ResultAssert.SucceededWithOptionalValue(await RecordAsync(rig, running, RoundResult.Succeeded, note: null));

		CardRound closed = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Single(round => round.Id == running.RoundId);
		Assert.NotNull(closed.Outcome);
		Assert.Null(closed.Note);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesAWorkRoundThatReportsFailure()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork work = await rig.ClaimAsync();
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, work, RoundResult.Failed, "I could not finish", "Details"));

		Assert.Contains("cannot fail", message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Errored", message, StringComparison.Ordinal);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(version, stored.Version);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		CardRound round = Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.Null(round.Outcome);
		Assert.Null(round.Note);
		Assert.Null(round.Report);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	public async Task RecordResultAsync_RefusesAnOutdatedResultFromARoundThatIsNotAMerge(RoundKind kind)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, running, RoundResult.Outdated, "main moved on"));

		Assert.Equal("Only a merge round can be outdated.", message);
		await AssertNothingChangedAndRoundStillOpenAsync(rig, card, running, version);
	}

	[Fact]
	public async Task RecordResultAsync_DoesNotRequireANoteForAnOutdatedMerge()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);

		PipelineWork? next = ResultAssert.SucceededWithOptionalValue(await RecordAsync(rig, merge, RoundResult.Outdated, note: null));

		Assert.NotNull(next);
		Assert.Equal(RoundKind.Gate, next.Kind);
		CardRound closed = (await TestData.ReadRoundsAsync(rig.Database, card.Id)).Single(round => round.Id == merge.RoundId);
		Assert.Equal(RoundOutcome.Outdated, closed.Outcome);
		Assert.Null(closed.Note);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Merge)]
	public async Task RecordResultAsync_RefusesAnUndefinedResult(RoundKind kind)
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork running = await rig.ClaimAtAsync(kind);
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, running, (RoundResult)99, "A note"));

		Assert.Contains("not recognized", message, StringComparison.OrdinalIgnoreCase);
		await AssertNothingChangedAndRoundStillOpenAsync(rig, card, running, version);
	}

	[Fact]
	public async Task RecordResultAsync_OfAnUnknownRound_ReportsNotFound()
	{
		await using PipelineRig rig = await ArrangeAsync();
		await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		await rig.ClaimAsync();

		string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(999, RoundResult.Succeeded, new RoundReport(null, null), TestContext.Current.CancellationToken));

		Assert.Equal("Round not found.", message);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesARoundThatWasAlreadyRecordedAndChangesNothing()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork work = await rig.ClaimAsync();
		PipelineWork gate = await rig.SucceedAsync(work, "First report");
		WorkCard? afterFirst = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(afterFirst);

		string message = ResultAssert.Failed(await RecordAsync(rig, work, RoundResult.Errored, "Late duplicate", "Second report"));

		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		await CardAssert.UnchangedAsync(rig.Database, afterFirst, expectedCardCount: 1);
		List<CardRound> rounds = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(2, rounds.Count);
		Assert.Equal(RoundOutcome.Completed, rounds[0].Outcome);
		Assert.Equal("First report", rounds[0].Report);
		Assert.Equal(gate.RoundId, rounds[1].Id);
		Assert.Null(rounds[1].Outcome);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesTheLastRoundOfACardThatWasMergedAndClosed()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork merge = await rig.ClaimAtAsync(RoundKind.Merge);
		await rig.RecordAsync(merge, RoundResult.Succeeded);
		ResultAssert.Succeeded(await rig.Cards.CloseAsync(card.Id, TestContext.Current.CancellationToken));
		WorkCard? closed = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(closed);

		string message = ResultAssert.Failed(await RecordAsync(rig, merge, RoundResult.Failed, "Too late"));

		Assert.Contains("no longer open", message, StringComparison.Ordinal);
		await CardAssert.UnchangedAsync(rig.Database, closed, expectedCardCount: 1);
	}

	[Fact]
	public async Task RecordResultAsync_AcceptsANoteAndAReportAtTheirLimits()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		string note = new('n', PyxieLimits.MaxRoundNoteLength);
		string report = new('r', PyxieLimits.MaxRoundReportLength);

		PipelineWork? next = await rig.RecordAsync(gate, RoundResult.Failed, note, report);

		Assert.NotNull(next);
		Assert.NotNull(next.Briefing);
		Assert.True(next.Briefing.Length <= PyxieLimits.MaxBriefingLength, "The next briefing must stay within its limit however long the report was.");
		CardRound stored = (await TestData.ReadRoundsAsync(rig.Database, card.Id))[1];
		Assert.Equal(note, stored.Note);
		Assert.Equal(report, stored.Report);
	}

	[Fact]
	public async Task RecordResultAsync_CountsTheNoteAfterTrimmingItsWhitespace()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);

		await rig.RecordAsync(gate, RoundResult.Failed, "  " + new string('n', PyxieLimits.MaxRoundNoteLength) + "  ");

		Assert.Equal(PyxieLimits.MaxRoundNoteLength, (await TestData.ReadRoundsAsync(rig.Database, card.Id))[1].Note?.Length);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesANoteThatIsTooLong()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, gate, RoundResult.Failed, new string('n', PyxieLimits.MaxRoundNoteLength + 1)));

		Assert.Contains("Note", message, StringComparison.Ordinal);
		Assert.Contains(PyxieLimits.MaxRoundNoteLength.ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
		await AssertNothingChangedAndRoundStillOpenAsync(rig, card, gate, version);
	}

	[Fact]
	public async Task RecordResultAsync_RefusesAReportThatIsTooLong()
	{
		await using PipelineRig rig = await ArrangeAsync();
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork gate = await rig.ClaimAtAsync(RoundKind.Gate);
		int version = (await TestData.FindCardAsync(rig.Database, card.Id))?.Version ?? -1;

		string message = ResultAssert.Failed(await RecordAsync(rig, gate, RoundResult.Succeeded, note: null, report: new string('r', PyxieLimits.MaxRoundReportLength + 1)));

		Assert.Contains("Report", message, StringComparison.Ordinal);
		Assert.Contains(PyxieLimits.MaxRoundReportLength.ToString(CultureInfo.InvariantCulture), message, StringComparison.Ordinal);
		await AssertNothingChangedAndRoundStillOpenAsync(rig, card, gate, version);
	}
}
