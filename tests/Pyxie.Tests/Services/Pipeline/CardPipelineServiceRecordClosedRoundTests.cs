using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary><c>RecordResultAsync</c> checks the result and the note against the round's kind before it checks that the round is still open, so a caller that reports something invalid for a round that is already closed is told what is wrong with the report, and a valid report for a closed round is told that the round is no longer open.</summary>
public sealed class CardPipelineServiceRecordClosedRoundTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<(PipelineRig Rig, PipelineWork Work)> ArrangeClosedWorkRoundAsync()
	{
		PipelineRig rig = new();

		try
		{
			await rig.CreateCardAsync("Card");
			PipelineWork work = await rig.ClaimAsync();
			await rig.RecordAsync(work, RoundResult.Succeeded, report: "Work done");

			return (rig, work);
		}
		catch
		{
			await rig.DisposeAsync();

			throw;
		}
	}

	[Fact]
	public async Task RecordResultAsync_ForAClosedRoundWithAResultItsKindCannotHave_ReportsTheInvalidResultAndNotThatTheRoundIsClosed()
	{
		(PipelineRig rig, PipelineWork work) = await ArrangeClosedWorkRoundAsync();
		await using (rig)
		{
			string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Failed, new RoundReport("It failed", null), Token));

			Assert.Equal("A work round cannot fail. Report Errored or Blocked with the reason instead.", message);
		}
	}

	[Fact]
	public async Task RecordResultAsync_ForAClosedRoundWithoutTheNoteItsResultNeeds_ReportsTheMissingNoteAndNotThatTheRoundIsClosed()
	{
		(PipelineRig rig, PipelineWork work) = await ArrangeClosedWorkRoundAsync();
		await using (rig)
		{
			string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Errored, new RoundReport(null, null), Token));

			Assert.Equal("A note is required when a round fails, is rejected, conflicts, errors, or is blocked.", message);
		}
	}

	[Fact]
	public async Task RecordResultAsync_ForAClosedRoundWithAValidResult_ReportsThatTheRoundIsNoLongerOpen()
	{
		(PipelineRig rig, PipelineWork work) = await ArrangeClosedWorkRoundAsync();
		await using (rig)
		{
			string message = ResultAssert.Failed(await rig.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, "Again"), Token));

			Assert.Equal("This round is no longer open.", message);
		}
	}
}
