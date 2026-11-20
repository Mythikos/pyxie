using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardTileRenderTests
{
	private static readonly DateTime s_now = new(2030, 6, 15, 12, 0, 0, DateTimeKind.Utc);

	[Fact]
	public async Task Render_ShowsTheCategoryInItsColorAndTheNameAsAButtonInsideTheHeading()
	{
		WorkCard card = Card(CardLane.Queue);
		card.Category = new Category { Id = 2, Name = "Bug", Color = CategoryColors.Pink };

		string html = await RenderAsync(Tile(card));

		Assert.Matches(CategoryChipPattern("pyx-chip pyx-chip-pink", "Bug"), html);
		Assert.Matches(@"<h3 class=""card-title""[^>]*><button type=""button"" class=""card-name""[^>]*>Fix the login</button></h3>", html);
	}

	[Fact]
	public async Task Render_ShowsACategoryInAColorOfItsOwnInThatColorWithTextDerivedForIt()
	{
		WorkCard card = Card(CardLane.Queue);
		card.Category = new Category { Id = 4, Name = "Docs", Color = HexColor.Parse("#E3F0FF") };

		string html = await RenderAsync(Tile(card));

		Assert.Matches(CategoryChipPattern("pyx-chip pyx-chip-custom", "Docs"), html);
		Assert.Contains($"style=\"--chip-tint: #E3F0FF; --chip-ink: {ChipInk.For(HexColor.Parse("#E3F0FF"))}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_LeavesOutTheDescriptionOfTheCard()
	{
		WorkCard card = Card(CardLane.Queue);
		card.Description = "Users cannot sign in with a plus sign.";

		string html = await RenderAsync(Tile(card));

		Assert.DoesNotContain("plus sign", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_OpensTheCardWhenTheTileOrItsNameIsClicked()
	{
		WorkCard card = Card(CardLane.Queue);
		List<WorkCard> opened = [];

		await using ComponentHarness harness = Harness();
		RenderedComponent<CardTile> tile = await harness.RenderAsync<CardTile>(Parameters(Tile(card), clicked: opened.Add));
		await tile.ClickAsync("OpenAsync");

		Assert.Same(card, Assert.Single(opened));
	}

	[Fact]
	public async Task Render_NamesTheWorkerAndTheReviewerOfACardInTheQueue()
	{
		WorkCard card = Card(CardLane.Queue);
		card.WorkerAgent = new Agent { Id = 2, Name = "Claude" };
		card.ReviewerAgent = new Agent { Id = 3, Name = "Codex" };

		string html = await RenderAsync(Tile(card));

		Assert.Matches(@"<div class=""card-assigned""[^>]*>\s*<span class=""card-worker""[^>]*>Claude</span>\s*<span aria-hidden=""true""[^>]*>·</span>\s*<span[^>]*>Codex</span>\s*</div>", html);
	}

	[Fact]
	public async Task Render_SaysNoReviewerForACardInTheQueueWithoutOne()
	{
		WorkCard card = Card(CardLane.Queue);
		card.WorkerAgent = new Agent { Id = 2, Name = "Claude" };

		string html = await RenderAsync(Tile(card));

		Assert.Matches(@"<span class=""card-worker""[^>]*>Claude</span>\s*<span aria-hidden=""true""[^>]*>·</span>\s*<span[^>]*>No reviewer</span>", html);
	}

	[Fact]
	public async Task Render_NamesTheAgentsAssignedNowOnACardInTheQueueNotTheOnesThatRanBefore()
	{
		WorkCard card = Card(CardLane.Queue);
		card.WorkerAgent = new Agent { Id = 2, Name = "New worker" };
		card.ReviewerAgent = new Agent { Id = 3, Name = "New reviewer" };

		string html = await RenderAsync(Tile(card, Round(1, RoundKind.Work, RoundOutcome.Completed, "Old worker"), Round(2, RoundKind.Review, RoundOutcome.Rejected, "Old reviewer")));

		Assert.Matches(@"<span class=""card-worker""[^>]*>New worker</span>\s*<span aria-hidden=""true""[^>]*>·</span>\s*<span[^>]*>New reviewer</span>", html);
		Assert.DoesNotContain("Old worker", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Old reviewer", html, StringComparison.Ordinal);
		Assert.DoesNotContain("round-trail", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsOnlyTheLimitQuietlyForACardThatHasNotRun()
	{
		string html = await RenderAsync(Tile(Card(CardLane.Queue, attemptLimit: 5)));

		Assert.Matches(@"<span class=""card-count card-count-idle""[^>]*>5</span>", html);
	}

	[Fact]
	public async Task Render_ShowsNoStageNoTrailAndNoButtonsOnACardInTheQueueThatIsReady()
	{
		WorkCard card = Card(CardLane.Queue);
		card.WorkerAgent = new Agent { Id = 2, Name = "Claude" };

		string html = await RenderAsync(Tile(card));

		Assert.DoesNotContain("stage-", html, StringComparison.Ordinal);
		Assert.DoesNotContain("round-trail", html, StringComparison.Ordinal);
		Assert.DoesNotContain("<button id=", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-tile-error", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-tile-active", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-tile-running", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_TintsACardThePipelineStoppedAndGivesItsReasonBesideAnAlertIcon()
	{
		string html = await RenderAsync(Stopped("Stopped after 5 attempts. Gates failed: 3 tests."));

		Assert.Matches(@"<article class=""card-tile card-tile-error""", html);
		Assert.Matches(@"<div class=""card-reason""[^>]*><span class=""anticon exclamation-circle""><svg [^>]*aria-hidden=""true""[^>]*>.*?</svg></span><span class=""card-reason-text""[^>]*>Stopped after 5 attempts\. Gates failed: 3 tests\.</span></div>", html);
	}

	[Fact]
	public async Task Render_ShowsTheReasonInPlaceOfAnErrorBadge()
	{
		string html = await RenderAsync(Stopped("The task is unclear."));

		Assert.DoesNotContain(">Error<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheAttemptsOfAStoppedCardInTheErrorColor()
	{
		string html = await RenderAsync(Stopped("The task is unclear.", attempt: 5, attemptLimit: 5));

		Assert.Matches(@"<span class=""card-count card-count-error""[^>]*>5/5</span>", html);
	}

	[Fact]
	public async Task Render_OffersRetryAndEditOnACardThePipelineStopped()
	{
		string html = await RenderAsync(Stopped("The task is unclear."));

		Assert.Matches(@"<div class=""card-actions"" __internal_stopPropagation_onclick[^>]*><button [^>]*class=""ant-btn ant-btn-color-primary ant-btn-variant-solid ant-btn-sm card-retry""[^>]*><span class=""anticon reload"">.*?</span>Retry</button><button [^>]*class=""ant-btn ant-btn-color-default ant-btn-variant-outlined ant-btn-sm card-edit""[^>]*>Edit</button></div>", html);
	}

	[Fact]
	public async Task Render_ShowsNoWorkerLineOnACardThePipelineStopped()
	{
		WorkCard card = Card(CardLane.Queue, state: CardState.Error, attempt: 1);
		card.WorkerAgent = new Agent { Id = 2, Name = "Claude" };

		string html = await RenderAsync(Tile(card, Round(1, RoundKind.Work, RoundOutcome.Blocked, "Claude", note: "The task is unclear.")));

		Assert.DoesNotContain("card-assigned", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-worker", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_RaisesTheCardWhenRetryOrEditIsClicked()
	{
		WorkCard card = Card(CardLane.Queue, state: CardState.Error, attempt: 1);
		List<WorkCard> opened = [];
		List<WorkCard> retried = [];

		await using ComponentHarness harness = Harness();
		RenderedComponent<CardTile> tile = await harness.RenderAsync<CardTile>(Parameters(Tile(card), clicked: opened.Add, retryClicked: retried.Add));
		await tile.ClickAsync("RetryAsync", new MouseEventArgs { Detail = 1 });
		await tile.ClickAsync("OpenAsync");

		Assert.Same(card, Assert.Single(retried));
		Assert.Same(card, Assert.Single(opened));
	}

	[Fact]
	public async Task Render_ShowsWhyARetryOrCloseWasRefusedOnTheCardInAnAlert()
	{
		string html = await RenderAsync(Stopped("The task is unclear."), refusal: "Only a card the pipeline stopped can be retried.");

		Assert.Matches(@"<div class=""card-refusal"" role=""alert""[^>]*>Only a card the pipeline stopped can be retried\.</div>", html);
	}

	[Fact]
	public async Task Render_ShowsNoAlertWhenNothingWasRefused()
	{
		string html = await RenderAsync(Stopped("The task is unclear."));

		Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheAttemptsOfACardThePipelineIsWorkingOnOverTheLimit()
	{
		WorkCard card = Card(CardLane.Gates, attempt: 2, attemptLimit: 5);

		string html = await RenderAsync(Tile(card, Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude"), Round(2, RoundKind.Gate, RoundOutcome.Failed)));

		Assert.Matches(@"<span class=""card-count""[^>]*>2/5</span>", html);
	}

	[Theory]
	[InlineData(RoundKind.Work, "pyx-chip pyx-chip-green", "Work")]
	[InlineData(RoundKind.Review, "pyx-chip pyx-chip-green", "Review")]
	[InlineData(RoundKind.Resolve, "pyx-chip pyx-chip-pink", "Resolve")]
	[InlineData(RoundKind.Merge, "pyx-chip pyx-chip-dark", "Merging")]
	public async Task Render_NamesTheStageOfTheRunningRoundInABadgeBeforeTheCategory(RoundKind kind, string chipClass, string text)
	{
		string html = await RenderAsync(Tile(Card(CardLane.InProgress, attempt: 1), Round(1, kind, outcome: null, agentName: "Claude")));

		Assert.Matches($@"<div class=""card-top""[^>]*><span class=""{chipClass}[^""]*""[^>]*>.*?{text}</span>{CategoryChipPattern("pyx-chip pyx-chip-green", "Feature")}", html);
	}

	[Fact]
	public async Task Render_ShowsTheMarkOnTheMergingBadge()
	{
		string html = await RenderAsync(Tile(Card(CardLane.Review, attempt: 1), Round(1, RoundKind.Merge, outcome: null)));

		Assert.Contains("src=\"brand/pyxie-mark-small.svg\" width=\"14\" height=\"14\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNoBadgeForARunningGateRound()
	{
		string html = await RenderAsync(Tile(Card(CardLane.Gates, attempt: 1), Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude"), Round(2, RoundKind.Gate, outcome: null)));

		Assert.Matches(@"<div class=""card-top""[^>]*>" + CategoryChipPattern("pyx-chip pyx-chip-green", "Feature"), html);
	}

	[Fact]
	public async Task Render_ShowsNoBadgeNoAgentAndNoElapsedTimeWhileNoRoundRuns()
	{
		WorkCard card = Card(CardLane.InProgress, attempt: 1);

		string html = await RenderAsync(Tile(card, Round(1, RoundKind.Work, RoundOutcome.Interrupted, "Claude")));

		Assert.Matches(@"<div class=""card-top""[^>]*>" + CategoryChipPattern("pyx-chip pyx-chip-green", "Feature"), html);
		Assert.DoesNotContain("card-agent", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-elapsed", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-tile-running", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Rounds: work interrupted\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_CastsTheRunningShadowOnlyOnACardWithARunningRound()
	{
		string running = await RenderAsync(Tile(Card(CardLane.InProgress, attempt: 1), Round(1, RoundKind.Work, outcome: null, agentName: "Claude")));
		string waiting = await RenderAsync(Tile(Card(CardLane.InProgress, attempt: 1), Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude")));

		Assert.Contains("card-tile card-tile-active card-tile-running", running, StringComparison.Ordinal);
		Assert.Contains("card-tile card-tile-active\"", waiting, StringComparison.Ordinal);
		Assert.DoesNotContain("card-tile-running", waiting, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheRoundTrailTheAgentRunningTheRoundAndHowLongItHasRun()
	{
		WorkCard card = Card(CardLane.InProgress, attempt: 2);
		BoardCard tile = Tile(card, Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude"), Round(2, RoundKind.Gate, RoundOutcome.Failed), Round(3, RoundKind.Work, outcome: null, agentName: "Claude", startedUtc: s_now.AddMinutes(-8)));

		string html = await RenderAsync(tile);

		Assert.Matches(@"<div class=""card-footer""[^>]*><div class=""round-trail"" role=""img"" aria-label=""Rounds: work completed, gate failed, work running""[^>]*>.*?</div><span class=""card-agent""[^>]*>Claude</span><span class=""card-elapsed""[^>]*>8m</span></div>", html);
	}

	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Merge)]
	public async Task Render_NamesNoAgentWhileARoundRunsThatRunsNone(RoundKind kind)
	{
		WorkCard card = Card(CardLane.Gates, attempt: 1);
		card.WorkerAgent = new Agent { Id = 2, Name = "Worker" };
		card.ReviewerAgent = new Agent { Id = 3, Name = "Reviewer" };

		string html = await RenderAsync(Tile(card, Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude"), Round(2, kind, outcome: null)));

		Assert.DoesNotContain("card-agent", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Worker", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Reviewer", html, StringComparison.Ordinal);
		Assert.Contains("card-elapsed", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0, "now")]
	[InlineData(8, "8m")]
	[InlineData(60, "1h")]
	[InlineData(65, "1h 5m")]
	[InlineData(3060, "2d 3h")]
	public async Task Render_ShowsHowLongTheRunningRoundHasRunInTheShortForm(int minutes, string expected)
	{
		string html = await RenderAsync(Tile(Card(CardLane.Review, attempt: 1), Round(1, RoundKind.Review, outcome: null, agentName: "Codex", startedUtc: s_now.AddMinutes(-minutes))));

		Assert.Matches($@"<span class=""card-elapsed""[^>]*>{expected}</span>", html);
	}

	[Fact]
	public async Task Render_ShowsNoTrailOnACardInALaneTheAgentsOwnWhenItHasNoRounds()
	{
		string html = await RenderAsync(Tile(Card(CardLane.InProgress, attempt: 1)));

		Assert.DoesNotContain("round-trail", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-footer", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsWhenACompleteCardWasMergedInPlaceOfTheAttempts()
	{
		BoardCard tile = Tile(Card(CardLane.Complete, attempt: 1), Round(1, RoundKind.Work, RoundOutcome.Completed, "Claude"), Round(2, RoundKind.Merge, RoundOutcome.Merged, startedUtc: s_now.AddHours(-2), endedUtc: s_now.AddHours(-2)));

		string html = await RenderAsync(tile);

		Assert.Matches(@"<span class=""card-finished""[^>]*>2 hours ago</span>", html);
		Assert.DoesNotContain("card-count", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNoFinishTimeForACompleteCardWithoutAFinishedRound()
	{
		string html = await RenderAsync(Tile(Card(CardLane.Complete, attempt: 1)));

		Assert.DoesNotContain("card-finished", html, StringComparison.Ordinal);
		Assert.DoesNotContain("card-count", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_SaysMergedWithACheckAndOffersToCloseACompleteCard()
	{
		BoardCard tile = Tile(Card(CardLane.Complete, attempt: 1), Round(1, RoundKind.Merge, RoundOutcome.Merged, startedUtc: s_now, endedUtc: s_now));

		string html = await RenderAsync(tile);

		Assert.Matches(@"<div class=""card-footer-complete""[^>]*><span class=""card-merged""[^>]*><span class=""anticon check""><svg [^>]*aria-hidden=""true""[^>]*>.*?</svg></span>Merged</span><span class=""card-action"" __internal_stopPropagation_onclick[^>]*><button [^>]*>Close</button></span></div>", html);
		Assert.DoesNotContain("round-trail", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_RaisesTheCardWhenCloseIsClicked()
	{
		WorkCard card = Card(CardLane.Complete, attempt: 1);
		List<WorkCard> closed = [];

		await using ComponentHarness harness = Harness();
		RenderedComponent<CardTile> tile = await harness.RenderAsync<CardTile>(Parameters(Tile(card), closeClicked: closed.Add));
		await tile.ClickAsync("CloseAsync", new MouseEventArgs { Detail = 1 });

		Assert.Same(card, Assert.Single(closed));
	}

	[Fact]
	public async Task Render_RaisesNothingForAPressThatIsPartOfADoubleClick()
	{
		WorkCard complete = Card(CardLane.Complete, attempt: 1);
		WorkCard stopped = Card(CardLane.Queue, state: CardState.Error, attempt: 1);
		List<WorkCard> closed = [];
		List<WorkCard> retried = [];

		// The board reads the cards again before the second click of a double click, so the next card has slid under the pointer by then.
		await using ComponentHarness harness = Harness();
		RenderedComponent<CardTile> closing = await harness.RenderAsync<CardTile>(Parameters(Tile(complete), closeClicked: closed.Add));
		RenderedComponent<CardTile> retrying = await harness.RenderAsync<CardTile>(Parameters(Tile(stopped), retryClicked: retried.Add));
		await closing.ClickAsync("CloseAsync", new MouseEventArgs { Detail = 2 });
		await retrying.ClickAsync("RetryAsync", new MouseEventArgs { Detail = 2 });

		Assert.Empty(closed);
		Assert.Empty(retried);
	}

	[Theory]
	[InlineData(CardLane.Queue)]
	[InlineData(CardLane.InProgress)]
	[InlineData(CardLane.Gates)]
	[InlineData(CardLane.Review)]
	public async Task Render_OffersNoCloseOnACardThatIsNotComplete(CardLane lane)
	{
		string html = await RenderAsync(Tile(Card(lane, attempt: 1), Round(1, RoundKind.Work, outcome: null, agentName: "Claude")));

		Assert.DoesNotContain(">Close<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Merged", html, StringComparison.Ordinal);
	}

	// A category's chip: its name in the chip's color, with the full name as the title and the name in an element of its own, so a name too long for the room is cut short with an ellipsis and can still be read.
	private static string CategoryChipPattern(string colorClasses, string name)
	{
		return $@"<span class=""{colorClasses}"" title=""{name}""[^>]*><span class=""chip-name""[^>]*>{name}</span></span>";
	}

	private static BoardCard Stopped(string reason, int attempt = 1, int attemptLimit = 5)
	{
		WorkCard card = Card(CardLane.Queue, state: CardState.Error, attempt: attempt, attemptLimit: attemptLimit);
		RoundSummary[] rounds = [Round(1, RoundKind.Work, RoundOutcome.Blocked, "Claude", note: reason)];

		return new BoardCard(card, CardHistorySummary.From(CardState.Error, rounds), rounds);
	}

	private static BoardCard Tile(WorkCard card, params RoundSummary[] rounds)
	{
		return new BoardCard(card, CardHistorySummary.From(card.State, rounds), rounds);
	}

	private static RoundSummary Round(int sequence, RoundKind kind, RoundOutcome? outcome, string? agentName = null, string? note = null, DateTime? startedUtc = null, DateTime? endedUtc = null)
	{
		return RoundSummary.From(TestRound.Create(sequence, kind, outcome, agentName, note, startedUtc: startedUtc ?? s_now, endedUtc: endedUtc ?? (outcome is null ? null : s_now)));
	}

	private static WorkCard Card(CardLane lane, CardState state = CardState.Ready, int attempt = 0, int attemptLimit = 5)
	{
		WorkCard card = InMemoryCard.Create(attempt: attempt, attemptLimit: attemptLimit, categoryName: "Feature");
		card.Category.Color = CategoryColors.Green;
		card.Lane = lane;
		card.State = state;

		return card;
	}

	private static ComponentHarness Harness()
	{
		return new ComponentHarness(services => services.AddSingleton<TimeProvider>(new ControlledTimeProvider(s_now)));
	}

	private static Dictionary<string, object> Parameters(BoardCard card, string? refusal = null, Action<WorkCard>? clicked = null, Action<WorkCard>? retryClicked = null, Action<WorkCard>? closeClicked = null)
	{
		Dictionary<string, object> parameters = new() { [nameof(CardTile.Card)] = card };

		if (refusal is not null)
		{
			parameters[nameof(CardTile.Refusal)] = refusal;
		}

		AddCallback(parameters, nameof(CardTile.Clicked), clicked);
		AddCallback(parameters, nameof(CardTile.RetryClicked), retryClicked);
		AddCallback(parameters, nameof(CardTile.CloseClicked), closeClicked);

		return parameters;
	}

	private static void AddCallback(Dictionary<string, object> parameters, string name, Action<WorkCard>? handler)
	{
		if (handler is not null)
		{
			parameters[name] = EventCallback.Factory.Create<WorkCard>(new object(), handler);
		}
	}

	private static async Task<string> RenderAsync(BoardCard card, string? refusal = null)
	{
		await using ComponentHarness harness = Harness();
		RenderedComponent<CardTile> tile = await harness.RenderAsync<CardTile>(Parameters(card, refusal));

		return await tile.HtmlAsync();
	}
}
