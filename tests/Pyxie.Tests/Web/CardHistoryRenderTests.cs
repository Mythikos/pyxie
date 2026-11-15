using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardHistoryRenderTests
{
	[Theory]
	[InlineData(RoundOutcome.Completed, "Completed", "success")]
	[InlineData(RoundOutcome.Passed, "Passed", "success")]
	[InlineData(RoundOutcome.Approved, "Approved", "success")]
	[InlineData(RoundOutcome.Resolved, "Resolved", "success")]
	[InlineData(RoundOutcome.Merged, "Merged", "success")]
	[InlineData(RoundOutcome.Failed, "Failed", "danger")]
	[InlineData(RoundOutcome.Rejected, "Rejected", "danger")]
	[InlineData(RoundOutcome.Conflicted, "Conflicted", "danger")]
	[InlineData(RoundOutcome.Errored, "Errored", "danger")]
	[InlineData(RoundOutcome.Blocked, "Blocked", "danger")]
	[InlineData(RoundOutcome.Outdated, "Outdated", "warning")]
	[InlineData(RoundOutcome.Skipped, "Skipped", "muted")]
	[InlineData(RoundOutcome.Interrupted, "Interrupted", "muted")]
	public async Task RoundLine_TonesTheOutcomeByWhatItMeans(RoundOutcome outcome, string text, string tone)
	{
		string html = await RenderRoundAsync(TestRound.Create(1, RoundKind.Work, outcome, "Claude", note: "Why."));

		Assert.Contains($"<span class=\"round-outcome round-tone-{tone}\"", html, StringComparison.Ordinal);
		Assert.Contains($">{text}</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RoundLine_GivesARunningRoundNoTone()
	{
		string html = await RenderRoundAsync(TestRound.Create(1, RoundKind.Work, outcome: null, agentName: "Claude"));

		Assert.Contains("round-tone-none", html, StringComparison.Ordinal);
		Assert.Contains(">Running</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RoundLine_ShowsTheAttemptTheRoundBelongsTo()
	{
		string html = await RenderRoundAsync(TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Claude", attempt: 2));

		Assert.Contains(">Attempt 2</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RoundLine_ShowsAnEmptyAttemptCellForARoundWithoutOneSoTheColumnsStayAligned()
	{
		string html = await RenderRoundAsync(TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Claude", attempt: 0));

		Assert.Contains("round-attempt", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Attempt 0", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ListsTheRoundsOldestFirstWithTheirAttemptsAndASummary()
	{
		List<CardRound> rounds =
		[
			TestRound.Create(3, RoundKind.Work, RoundOutcome.Completed, "Claude", attempt: 2),
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Claude"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Failed, note: "Tests failed."),
			TestRound.Create(4, RoundKind.Gate, outcome: null, attempt: 2),
		];
		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds.Select(RoundSummary.From));

		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CardHistory> history = await harness.RenderAsync<CardHistory>(new Dictionary<string, object> { [nameof(CardHistory.Summary)] = summary, [nameof(CardHistory.Rounds)] = rounds });
		string html = await history.HtmlAsync();

		Assert.Contains("2 work, 2 gate, 0 review, 1 sent back, running", html, StringComparison.Ordinal);
		int[] positions = [.. new[] { ">Attempt 1</span>", ">Failed</span>", ">Attempt 2</span>", ">Running</span>" }.Select(text => html.IndexOf(text, StringComparison.Ordinal))];
		Assert.DoesNotContain(-1, positions);
		Assert.Equal(positions.Order(), positions);
	}

	[Fact]
	public async Task RoundLine_ShowsAResolveRoundByItsKindAndTheMergeAgentThatRanIt()
	{
		string html = await RenderRoundAsync(TestRound.Create(3, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", attempt: 2));

		Assert.Contains(">Resolve</span>", html, StringComparison.Ordinal);
		Assert.Contains(">Merger</span>", html, StringComparison.Ordinal);
		Assert.Contains(">Resolved</span>", html, StringComparison.Ordinal);
		Assert.Contains(">Attempt 2</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ListsAConflictAndItsResolutionInTheOrderTheyHappened()
	{
		List<CardRound> rounds =
		[
			TestRound.Create(1, RoundKind.Work, RoundOutcome.Completed, "Claude"),
			TestRound.Create(2, RoundKind.Gate, RoundOutcome.Passed),
			TestRound.Create(3, RoundKind.Review, RoundOutcome.Approved, "Codex"),
			TestRound.Create(4, RoundKind.Merge, RoundOutcome.Conflicted, note: "Merging main into pyxie/card-1 conflicts in a.txt."),
			TestRound.Create(5, RoundKind.Resolve, RoundOutcome.Resolved, "Merger", note: "Combined both changes.", report: "Kept both lines.", attempt: 2),
			TestRound.Create(6, RoundKind.Gate, outcome: null, attempt: 2),
		];
		CardHistorySummary summary = CardHistorySummary.From(CardState.Ready, rounds.Select(RoundSummary.From));

		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<CardHistory> history = await harness.RenderAsync<CardHistory>(new Dictionary<string, object> { [nameof(CardHistory.Summary)] = summary, [nameof(CardHistory.Rounds)] = rounds });
		string html = await history.HtmlAsync();

		int[] positions = [.. new[] { ">Conflicted</span>", ">Resolve</span>", ">Resolved</span>", ">Running</span>" }.Select(text => html.IndexOf(text, StringComparison.Ordinal))];
		Assert.DoesNotContain(-1, positions);
		Assert.Equal(positions.Order(), positions);
		Assert.Contains("Combined both changes.", html, StringComparison.Ordinal);
		Assert.Contains("1 work, 2 gate, 1 review, 1 resolve, 1 sent back, running", html, StringComparison.Ordinal);
	}

	private static async Task<string> RenderRoundAsync(CardRound round)
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<RoundLine> line = await harness.RenderAsync<RoundLine>(new Dictionary<string, object> { [nameof(RoundLine.Round)] = round });

		return await line.HtmlAsync();
	}
}
