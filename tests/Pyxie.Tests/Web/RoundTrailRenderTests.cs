using System.Text.RegularExpressions;

using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RoundTrailRenderTests
{
	[Fact]
	public async Task Render_ShowsOneSquareForEachRoundOldestFirstToneByOutcomeAndTheRunningRoundAsAnOutline()
	{
		string html = await RenderAsync(Round(1, RoundKind.Work, RoundOutcome.Completed), Round(2, RoundKind.Gate, RoundOutcome.Failed), Round(3, RoundKind.Work, outcome: null));

		Assert.Equal(["trail-square trail-tone-success", "trail-square trail-tone-danger", "trail-square trail-open trail-stage-work"], Squares(html));
	}

	[Theory]
	[InlineData(RoundOutcome.Completed, "success")]
	[InlineData(RoundOutcome.Passed, "success")]
	[InlineData(RoundOutcome.Approved, "success")]
	[InlineData(RoundOutcome.Merged, "success")]
	[InlineData(RoundOutcome.Resolved, "success")]
	[InlineData(RoundOutcome.Failed, "danger")]
	[InlineData(RoundOutcome.Rejected, "danger")]
	[InlineData(RoundOutcome.Conflicted, "danger")]
	[InlineData(RoundOutcome.Errored, "danger")]
	[InlineData(RoundOutcome.Blocked, "danger")]
	[InlineData(RoundOutcome.Skipped, "muted")]
	[InlineData(RoundOutcome.Interrupted, "muted")]
	[InlineData(RoundOutcome.Outdated, "warning")]
	public async Task Render_TonesASquareByTheOutcomeOfItsRound(RoundOutcome outcome, string tone)
	{
		string html = await RenderAsync(Round(1, RoundKind.Work, outcome));

		Assert.Equal([$"trail-square trail-tone-{tone}"], Squares(html));
	}

	[Theory]
	[InlineData(RoundKind.Work, "work")]
	[InlineData(RoundKind.Review, "work")]
	[InlineData(RoundKind.Gate, "gate")]
	[InlineData(RoundKind.Resolve, "resolve")]
	[InlineData(RoundKind.Merge, "merge")]
	public async Task Render_OutlinesTheRunningRoundInTheColorOfItsStage(RoundKind kind, string stage)
	{
		string html = await RenderAsync(Round(1, kind, outcome: null));

		Assert.Equal([$"trail-square trail-open trail-stage-{stage}"], Squares(html));
	}

	[Fact]
	public async Task Render_ListsEveryRoundInWordsForAScreenReader()
	{
		string html = await RenderAsync(Round(1, RoundKind.Work, RoundOutcome.Completed), Round(2, RoundKind.Gate, RoundOutcome.Failed), Round(3, RoundKind.Work, outcome: null));

		Assert.Matches(@"<div class=""round-trail"" role=""img"" aria-label=""Rounds: work completed, gate failed, work running""", html);
	}

	[Fact]
	public async Task Render_WordsEveryKindAndOutcomeInLowerCase()
	{
		string html = await RenderAsync(
			Round(1, RoundKind.Work, RoundOutcome.Completed),
			Round(2, RoundKind.Gate, RoundOutcome.Passed),
			Round(3, RoundKind.Review, RoundOutcome.Approved),
			Round(4, RoundKind.Merge, RoundOutcome.Conflicted),
			Round(5, RoundKind.Resolve, RoundOutcome.Resolved),
			Round(6, RoundKind.Gate, RoundOutcome.Skipped),
			Round(7, RoundKind.Merge, RoundOutcome.Interrupted),
			Round(8, RoundKind.Merge, outcome: null));

		Assert.Contains("aria-label=\"Rounds: work completed, gate passed, review approved, merge conflicted, resolve resolved, gate skipped, merge interrupted, merge running\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsOnlyTheLatestTwentyRoundsInTheSquaresAndInTheWords()
	{
		Assert.Equal(20, RoundTrail.MaxShown);
		RoundSummary[] rounds = [.. Enumerable.Range(1, 25).Select(sequence => Round(sequence, sequence % 2 == 1 ? RoundKind.Work : RoundKind.Gate, sequence == 5 ? RoundOutcome.Failed : RoundOutcome.Completed))];

		string html = await RenderAsync(rounds);

		Assert.Equal(20, Squares(html).Count);
		string label = Regex.Match(html, @"aria-label=""([^""]*)""").Groups[1].Value;
		Assert.Equal(20, label.Split(", ").Length);
		Assert.StartsWith("Rounds: gate completed, work completed", label, StringComparison.Ordinal);
		Assert.DoesNotContain("failed", label, StringComparison.Ordinal);
		Assert.DoesNotContain("trail-tone-danger", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsEveryRoundOfATrailAtTheLimit()
	{
		RoundSummary[] rounds = [.. Enumerable.Range(1, RoundTrail.MaxShown).Select(sequence => Round(sequence, RoundKind.Work, sequence == 1 ? RoundOutcome.Failed : RoundOutcome.Completed))];

		string html = await RenderAsync(rounds);

		Assert.Equal(RoundTrail.MaxShown, Squares(html).Count);
		Assert.Equal("trail-square trail-tone-danger", Squares(html)[0]);
	}

	private static RoundSummary Round(int sequence, RoundKind kind, RoundOutcome? outcome)
	{
		return RoundSummary.From(TestRound.Create(sequence, kind, outcome));
	}

	private static List<string> Squares(string html)
	{
		return [.. Regex.Matches(html, @"<span class=""(trail-square[^""]*)""").Select(match => match.Groups[1].Value)];
	}

	private static async Task<string> RenderAsync(params RoundSummary[] rounds)
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<RoundTrail> trail = await harness.RenderAsync<RoundTrail>(new Dictionary<string, object> { [nameof(RoundTrail.Rounds)] = rounds });

		return await trail.HtmlAsync();
	}
}
