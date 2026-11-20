using System.Text.RegularExpressions;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Rules;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;
using Pyxie.Web.Components.Projects;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class BoardRenderTests
{
	private const string NotStopped = "Only a card the pipeline stopped can be retried.";
	private const string AlreadyClosed = "Card is already closed.";

	[Fact]
	public async Task Render_ShowsEachCardInItsLane()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");
		await TestData.AddCardAsync(database, "Working card", CardLane.InProgress, attempt: 1);

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		Assert.Contains("Queued card", html, StringComparison.Ordinal);
		Assert.Contains("Working card", html, StringComparison.Ordinal);
		Assert.True(html.IndexOf("Queued card", StringComparison.Ordinal) < html.IndexOf("Working card", StringComparison.Ordinal), "The Queue lane comes before In Progress.");
	}

	[Fact]
	public async Task Render_ShowsTheBoardLanesInOrderInAGridThatScrollsSideways()
	{
		await using TestDatabase database = new();

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		List<string> lanes = [.. Regex.Matches(html, @"<section class=""lane"" aria-label=""([^""]+)""").Select(match => match.Groups[1].Value)];
		Assert.Equal(CardLifecycleRules.BoardLanes.Select(lane => lane.DisplayName()), lanes);
		Assert.Matches(@"<div class=""board-scroll""[^>]*><div class=""board"" style=""--lane-count: 5""", html);
	}

	[Fact]
	public async Task Render_ShowsEachLaneWithItsColoredDotItsNameAndItsCardCount()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued one");
		await TestData.AddCardAsync(database, "Queued two");
		await TestData.AddCardAsync(database, "Working card", CardLane.InProgress, attempt: 1);
		await TestData.AddCardAsync(database, "Reviewed card", CardLane.Review, attempt: 1);
		await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		(string Name, string Dot, int Count)[] expected = [("Queue", "queue", 2), ("In Progress", "in-progress", 1), ("Gates", "gates", 0), ("Review", "review", 1), ("Complete", "complete", 1)];
		foreach ((string name, string dot, int count) in expected)
		{
			Assert.Matches($@"<section class=""lane"" aria-label=""{name}""[^>]*><header[^>]*><span class=""lane-dot lane-dot-{dot}""[^>]*></span>\s*<h2 class=""lane-title""[^>]*>{name}</h2>\s*<span class=""lane-count""[^>]*>{count}</span>", html);
		}
	}

	[Fact]
	public async Task Render_OffersAddOnTheQueueLaneOnly()
	{
		await using TestDatabase database = new();

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		Assert.Equal(1, Regex.Count(html, "lane-add"));
		Assert.True(html.IndexOf("lane-add", StringComparison.Ordinal) < html.IndexOf("aria-label=\"In Progress\"", StringComparison.Ordinal), "Add is in the Queue lane, the first.");
		Assert.Matches(@"class=""[^""]*lane-add""[^>]*><span class=""anticon plus"">.*?</span>Add</button>", html);
	}

	[Fact]
	public async Task Render_LeavesAClosedCardOffTheBoard()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Closed card", CardLane.Closed);

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		Assert.DoesNotContain("Closed card", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Closed\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_MarksACardThePipelineStoppedInRedWithItsReasonAndOffersRetry()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Stopped card", state: CardState.Error, attempt: 1);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Claude", "The task is unclear.");

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		Assert.Contains("card-tile-error", html, StringComparison.Ordinal);
		Assert.Contains("The task is unclear.", html, StringComparison.Ordinal);
		Assert.Contains(">Edit</button>", html, StringComparison.Ordinal);
		Assert.Matches(@"<button [^>]*card-retry""[^>]*>.*?Retry</button>", html);
	}

	[Fact]
	public async Task Render_ShowsTheTrailOfACardAnAgentIsWorkingOnWithTheAgentAndHowLongTheRoundHasRun()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Working card", CardLane.InProgress, attempt: 2);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Claude");
		await TestData.AddRoundAsync(database, card.Id, 2, RoundKind.Gate, RoundOutcome.Failed);
		await TestData.AddRoundAsync(database, card.Id, 3, RoundKind.Work, outcome: null, "Claude", attempt: 2);

		await using BoardPage page = await BoardPage.RenderAsync(database);

		string html = await page.Board.HtmlAsync();
		Assert.Contains("aria-label=\"Rounds: work completed, gate failed, work running\"", html, StringComparison.Ordinal);
		Assert.Matches(@"<span class=""card-agent""[^>]*>Claude</span><span class=""card-elapsed""[^>]*>now</span>", html);
		Assert.Matches(@"<span class=""card-count""[^>]*>2/5</span>", html);
	}

	[Fact]
	public async Task Announce_ReloadsTheBoardWhenCardsChange()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		await TestData.AddCardAsync(database, "Added by someone else");

		page.Activity.NotifyCardsChanged();

		await page.Board.WaitForHtmlAsync(html => html.Contains("Added by someone else", StringComparison.Ordinal), "The board should show the card another board added");
	}

	[Fact]
	public async Task Announce_AFloodOfChangesCostsOneReadInFlightAndOneBehindIt()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		CallProbe reads = page.Cards.BoardReads;
		reads.Hold(2);

		page.Activity.NotifyCardsChanged();
		await reads.WaitForStartAsync(2);

		for (int announcement = 0; announcement < 20; announcement++)
		{
			page.Activity.NotifyCardsChanged();
		}

		await TestData.AddCardAsync(database, "Added during the flood");
		reads.Release(2);
		await page.Board.WaitForHtmlAsync(html => html.Contains("Added during the flood", StringComparison.Ordinal), "The board should show the card added during the flood");
		await reads.WaitForStartAsync(3);
		await page.Board.RunAsync(_ => { });

		Assert.Equal(3, reads.Started);
		Assert.Equal(1, reads.MostAtOnce);
	}

	[Fact]
	public async Task Changed_ByTheDialogAnnouncesItSoOtherBoardsRefreshAndTheBoardShowsIt()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		int announcements = 0;
		page.Activity.CardsChanged += () => Interlocked.Increment(ref announcements);
		await page.Board.ClickAsync("OpenEditor", null, page.OpenProject.Current?.Id);
		CardEditor dialog = page.Board.ReadField<CardEditor>("_editor");
		await TestData.AddCardAsync(database, "Saved in the dialog");

		await page.Board.RunAsync(_ => dialog.Changed.InvokeAsync());

		Assert.Equal(1, announcements);
		await page.Board.WaitForHtmlAsync(html => html.Contains("Saved in the dialog", StringComparison.Ordinal), "The board should show the card the dialog saved");
	}

	[Fact]
	public async Task Retry_MakesAStoppedCardReadyAnnouncesTheChangeAndTheBoardShowsIt()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database, resumeRound: RoundKind.Gate);
		await using BoardPage page = await BoardPage.RenderAsync(database);
		int announcements = 0;
		page.Activity.CardsChanged += () => Interlocked.Increment(ref announcements);

		await page.RetryAsync(card);

		WorkCard stored = await ReadAsync(database, card.Id);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(RoundKind.Gate, stored.ResumeRound);
		Assert.Equal(1, announcements);
		string html = await page.Board.WaitForHtmlAsync(markup => !markup.Contains("card-tile-error", StringComparison.Ordinal), "The board should show the card as ready once it was retried");
		Assert.DoesNotContain("role=\"alert\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Retry_OfACardSomeoneElseAlreadyRetried_ShowsTheRefusalOnThatCardAndAnnouncesNothing()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await AddStoppedCardAsync(database, "Stopped card");
		await TestData.AddCardAsync(database, "Another card");
		await using BoardPage page = await BoardPage.RenderAsync(database);
		ResultAssert.Succeeded(await page.Cards.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));
		int announcements = 0;
		page.Activity.CardsChanged += () => Interlocked.Increment(ref announcements);

		await page.RetryAsync(stopped);

		string html = await page.Board.HtmlAsync();
		Assert.Equal(1, Regex.Count(html, "role=\"alert\""));
		Assert.Matches($@"Stopped card</button></h3>(?:(?!</article>).)*?<div class=""card-refusal"" role=""alert""[^>]*>{NotStopped}</div></article>", html);
		Assert.Equal(0, announcements);
	}

	[Fact]
	public async Task Retry_RefusalStaysUntilAChangeSucceeds()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await AddStoppedCardAsync(database, "Stopped card");
		WorkCard done = await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		await using BoardPage page = await BoardPage.RenderAsync(database);
		ResultAssert.Succeeded(await page.Cards.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));
		await page.RetryAsync(stopped);
		Assert.Contains(NotStopped, await page.Board.HtmlAsync(), StringComparison.Ordinal);

		await page.CloseAsync(done);

		Assert.DoesNotContain("role=\"alert\"", await page.Board.HtmlAsync(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Retry_RefusalGoesWhenTheBoardIsReadAgain()
	{
		await using TestDatabase database = new();
		WorkCard stopped = await AddStoppedCardAsync(database, "Stopped card");
		await using BoardPage page = await BoardPage.RenderAsync(database);
		ResultAssert.Succeeded(await page.Cards.RetryAsync(stopped.Id, TestContext.Current.CancellationToken));
		await page.RetryAsync(stopped);
		Assert.Contains(NotStopped, await page.Board.HtmlAsync(), StringComparison.Ordinal);

		page.Activity.NotifyCardsChanged();

		await page.Board.WaitForHtmlAsync(html => !html.Contains("role=\"alert\"", StringComparison.Ordinal), "The refusal should go once the board is read again");
	}

	[Fact]
	public async Task CloseFromAnOlderBoardLeavesTheCurrentProjectsCompleteCardAlone()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Done", CardLane.Complete, attempt: 1);
		await using BoardPage page = await BoardPage.RenderAsync(database);
		int currentId = Assert.IsType<Project>(page.OpenProject.Current).Id;
		await page.Board.ClickAsync("CloseAsync", card, currentId + 1);
		Assert.Equal(CardLane.Complete, Assert.IsType<WorkCard>(await TestData.FindCardAsync(database, card.Id)).Lane);
	}

	[Fact]
	public async Task Close_ClosesACompleteCardAnnouncesTheChangeAndTheBoardDropsIt()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		await using BoardPage page = await BoardPage.RenderAsync(database);
		int announcements = 0;
		page.Activity.CardsChanged += () => Interlocked.Increment(ref announcements);

		await page.CloseAsync(card);

		Assert.Equal(CardLane.Closed, (await ReadAsync(database, card.Id)).Lane);
		Assert.Equal(1, announcements);
		await page.Board.WaitForHtmlAsync(html => !html.Contains("Done card", StringComparison.Ordinal), "The board should drop the card that was closed out");
	}

	[Fact]
	public async Task Close_OfACardThatWasAlreadyClosed_ShowsTheRefusalOnThatCardAndAnnouncesNothing()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Done card", CardLane.Complete, attempt: 1);
		await using BoardPage page = await BoardPage.RenderAsync(database);
		ResultAssert.Succeeded(await page.Cards.CloseAsync(card.Id, TestContext.Current.CancellationToken));
		int announcements = 0;
		page.Activity.CardsChanged += () => Interlocked.Increment(ref announcements);

		await page.CloseAsync(card);

		string html = await page.Board.HtmlAsync();
		Assert.Matches($@"Done card</button></h3>(?:(?!</article>).)*?<div class=""card-refusal"" role=""alert""[^>]*>{AlreadyClosed}</div></article>", html);
		Assert.Equal(0, announcements);
	}

	[Fact]
	public async Task Tick_RefreshesTheElapsedTimeOfARunningRoundEveryMinuteWithoutReadingTheBoardAgain()
	{
		await using TestDatabase database = new();
		ControlledTimeProvider time = new(database.Clock.UtcNow);
		WorkCard card = await TestData.AddCardAsync(database, "Working card", CardLane.InProgress, attempt: 1);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, outcome: null, "Claude");
		await using BoardPage page = await BoardPage.RenderAsync(database, time: time);
		Assert.Matches(@"<span class=""card-elapsed""[^>]*>now</span>", await page.Board.HtmlAsync());
		Assert.Equal([TimeSpan.FromMinutes(1)], time.Periods);
		int reads = page.Cards.BoardReads.Started;

		time.Clock.Advance(TimeSpan.FromMinutes(8));
		time.FireTimers();

		await page.Board.WaitForHtmlAsync(html => Regex.IsMatch(html, @"<span class=""card-elapsed""[^>]*>8m</span>"), "The elapsed time should follow the clock when the minute timer fires");
		Assert.Equal(reads, page.Cards.BoardReads.Started);
	}

	[Fact]
	public async Task Dispose_StopsTheMinuteTimer()
	{
		await using TestDatabase database = new();
		ControlledTimeProvider time = new(database.Clock.UtcNow);
		await using BoardPage page = await BoardPage.RenderAsync(database, time: time);
		Assert.Equal(1, time.PendingTimers);

		await page.Board.RunAsync(board => board.Dispose());

		Assert.Equal(0, time.PendingTimers);
		time.FireTimers();
		await page.Board.RunAsync(_ => { });
	}

	[Fact]
	public async Task Render_SaysNoProjectIsOpenLinksToTheRepositoryPageAndReadsNoCards()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");

		await using BoardPage page = await BoardPage.RenderAsync(database, new OpenProject());

		string html = await page.Board.HtmlAsync();
		Assert.Contains(NoProjectOpen.Text, html, StringComparison.Ordinal);
		Assert.Contains("href=\"repository\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Queued card", html, StringComparison.Ordinal);
		Assert.DoesNotContain("class=\"board\"", html, StringComparison.Ordinal);
		Assert.Equal(0, page.Cards.BoardReads.Started);
	}

	[Fact]
	public async Task Announce_ShowsTheBoardOnceAProjectIsOpened()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");
		await using BoardPage page = await BoardPage.RenderAsync(database, new OpenProject());

		page.OpenProject.Set(AnotherProject());
		page.Activity.NotifyProjectChanged();

		string html = await page.Board.WaitForHtmlAsync(markup => markup.Contains("Queued card", StringComparison.Ordinal), "The board should show the cards of the project that was opened");
		Assert.DoesNotContain(NoProjectOpen.Text, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Announce_SaysNoProjectIsOpenOnceTheProjectIsClosed()
	{
		await using TestDatabase database = new();
		await TestData.AddCardAsync(database, "Queued card");
		await using BoardPage page = await BoardPage.RenderAsync(database);

		page.OpenProject.Set(null);
		page.Activity.NotifyProjectChanged();

		string html = await page.Board.WaitForHtmlAsync(markup => markup.Contains(NoProjectOpen.Text, StringComparison.Ordinal), "The board should say no project is open once the project was closed");
		Assert.DoesNotContain("Queued card", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Announce_GivesAnotherOpenedProjectACardDialogOfItsOwn()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		CardEditor before = page.Board.ReadField<CardEditor>("_editor");

		page.OpenProject.Set(AnotherProject());
		page.Activity.NotifyProjectChanged();

		await page.Board.WaitForHtmlAsync(_ => !ReferenceEquals(before, page.Board.ReadField<CardEditor>("_editor")), "The board should replace the card dialog of the project that is no longer open");
	}

	[Fact]
	public async Task Dispose_StopsListeningToAnnouncements()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		Assert.Equal(1, EventSubscribers.Count(page.Activity, nameof(PipelineActivity.CardsChanged)));
		Assert.Equal(1, EventSubscribers.Count(page.Activity, nameof(PipelineActivity.ProjectChanged)));

		await page.Board.RunAsync(board => board.Dispose());

		Assert.Equal(0, EventSubscribers.Count(page.Activity, nameof(PipelineActivity.CardsChanged)));
		Assert.Equal(0, EventSubscribers.Count(page.Activity, nameof(PipelineActivity.ProjectChanged)));
	}

	[Fact]
	public async Task Dispose_StopsReloadingWhenCardsChange()
	{
		await using TestDatabase database = new();
		await using BoardPage page = await BoardPage.RenderAsync(database);
		int before = page.Cards.BoardReads.Started;

		await page.Board.RunAsync(board => board.Dispose());
		page.Activity.NotifyCardsChanged();
		await page.Board.RunAsync(_ => { });

		Assert.Equal(before, page.Cards.BoardReads.Started);
	}

	// A project other than the stand-in the board opens with; the test database stands in for it too.
	private static Project AnotherProject()
	{
		return new Project { Id = 7, Name = "app", Folder = Path.Combine(Path.GetTempPath(), "pyxie-app"), IsOpen = true };
	}

	private static async Task<WorkCard> AddStoppedCardAsync(TestDatabase database, string name = "Stopped card", RoundKind? resumeRound = null)
	{
		WorkCard card = await TestData.AddCardAsync(database, name, state: CardState.Error, attempt: 1, resumeRound: resumeRound);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Claude", "The task is unclear.");

		return card;
	}

	private static async Task<WorkCard> ReadAsync(TestDatabase database, int id)
	{
		return await TestData.FindCardAsync(database, id) ?? throw new InvalidOperationException($"Card {id} is gone.");
	}
}
