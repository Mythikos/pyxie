using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardEditorRenderTests
{
	private const string StopReason = "The task is unclear.";
	private const string NotStopped = "Only a card the pipeline stopped can be retried.";

	[Fact]
	public async Task Open_ANewCard_MarksTheWorkerRequiredAndOffersAttempts()
	{
		await using TestDatabase database = new();
		await using CardDialog dialog = await CardDialog.OpenAsync(database);

		string html = await dialog.Editor.HtmlAsync();

		Assert.Matches(@"<label[^>]*class=""ant-form-item-required""[^>]*>Worker</label>", html);
		Assert.Matches(@"<label[^>]*>Attempts</label>", html);
		Assert.Contains("ant-input-number", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Retry<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("ant-alert", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Open_AStoppedCard_ShowsItsReasonInAnAlertAndOffersRetry()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		string html = await dialog.Editor.HtmlAsync();

		Assert.Matches($@"<div class=""ant-alert ant-alert-error[^""]*""[^>]*><div class=""ant-alert-section"">{StopReason}</div></div>", html);
		Assert.Contains(">Retry</button>", html, StringComparison.Ordinal);
		Assert.Contains(">Save</button>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Open_ANewCard_OffersCancelAndNoSecondWayToCloseTheDialog()
	{
		await using TestDatabase database = new();
		await using CardDialog dialog = await CardDialog.OpenAsync(database);

		string html = await dialog.Editor.HtmlAsync();

		Assert.Contains(">Cancel</button>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("ant-modal-close", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Close\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Open_ACardForReviewOnly_OffersBackAndNoSecondWayToCloseTheDialog()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", CardLane.InProgress);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		string html = await dialog.Editor.HtmlAsync();

		Assert.Contains(">Back</button>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("ant-modal-close", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Close\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Open_AReadyCardInTheQueue_ShowsNoAlertAndOffersNoRetry()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		string html = await dialog.Editor.HtmlAsync();

		Assert.DoesNotContain("ant-alert", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Retry<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Open_ACardThePipelineOwns_ShowsItsAttemptsAsTextAndOffersNothingToEdit()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", CardLane.InProgress, attempt: 2, attemptLimit: 3);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		string html = await dialog.Editor.HtmlAsync();

		Assert.Matches(@"<input[^>]*\breadonly\b[^>]*value=""2 of 3""", html);
		Assert.DoesNotContain("ant-input-number", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Retry<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Save<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Retry_MakesAStoppedCardReadyAndAnnouncesTheChange()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database, resumeRound: RoundKind.Gate);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		await dialog.Editor.ClickAsync("RetryAsync");

		WorkCard stored = await ReadAsync(database, card.Id);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(RoundKind.Gate, stored.ResumeRound);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Retry_SavesWhatThePersonChangedFirstSoTheCardStartsAgainAtWork()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database, resumeRound: RoundKind.Gate);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().Description = "Spell out which sign-in fails.";

		await dialog.Editor.ClickAsync("RetryAsync");

		WorkCard stored = await ReadAsync(database, card.Id);
		Assert.Equal("Spell out which sign-in fails.", stored.Description);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Null(stored.ResumeRound);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Retry_RetriesNothingWhileWhatThePersonChangedCannotBeSaved()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().Description = "   ";

		await dialog.Editor.ClickAsync("RetryAsync");

		string html = await dialog.Editor.HtmlAsync();
		Assert.Contains("Description is required.", html, StringComparison.Ordinal);
		Assert.Equal(CardState.Error, (await ReadAsync(database, card.Id)).State);
		Assert.Equal(0, dialog.Changes);
	}

	[Fact]
	public async Task Retry_OfACardSomeoneElseAlreadyRetried_ShowsTheRefusalAndStopsOfferingIt()
	{
		await using TestDatabase database = new();
		WorkCard card = await AddStoppedCardAsync(database);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		ResultAssert.Succeeded(await dialog.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));

		await dialog.Editor.ClickAsync("RetryAsync");

		string html = await dialog.Editor.HtmlAsync();
		Assert.Contains(NotStopped, html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Retry<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("ant-alert", html, StringComparison.Ordinal);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Save_RequiresAWorkerLikeACategory()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", workerAgentId: null);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		await dialog.Editor.ClickAsync("SaveAsync");

		string html = await dialog.Editor.HtmlAsync();
		Assert.Contains("Worker is required.", html, StringComparison.Ordinal);
		Assert.Equal(card.Version, (await ReadAsync(database, card.Id)).Version);
		Assert.Equal(0, dialog.Changes);
	}

	[Fact]
	public async Task Save_HidesTheWorkerMessageUntilTheFirstAttempt()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", workerAgentId: null);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		string html = await dialog.Editor.HtmlAsync();

		Assert.DoesNotContain("Worker is required.", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(PyxieLimits.MaxAttemptLimit + 1)]
	public async Task Save_RefusesAttemptsOutOfRangeWithTheServiceMessage(int attempts)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().AttemptLimit = attempts;

		await dialog.Editor.ClickAsync("SaveAsync");

		string html = await dialog.Editor.HtmlAsync();
		Assert.Contains($"Attempts must be between 1 and {PyxieLimits.MaxAttemptLimit}.", html, StringComparison.Ordinal);
		Assert.Equal(PyxieLimits.DefaultAttemptLimit, (await ReadAsync(database, card.Id)).AttemptLimit);
		Assert.Equal(0, dialog.Changes);
	}

	[Fact]
	public async Task Save_StoresTheAttemptLimitTheCardWasGiven()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().AttemptLimit = 7;

		await dialog.Editor.ClickAsync("SaveAsync");

		Assert.Equal(7, (await ReadAsync(database, card.Id)).AttemptLimit);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Save_CreatesANewCardWithTheChosenWorkerAndAttempts()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.EnsureDefaultAgentAsync(database);
		await using CardDialog dialog = await CardDialog.OpenAsync(database);
		CardFormModel form = dialog.Form();
		form.Name = "Fix the login";
		form.Description = "Users cannot sign in.";
		form.CategoryId = PyxieSeedData.FeatureCategoryId;
		form.WorkerAgentId = worker.Id;
		form.AttemptLimit = 4;

		await dialog.Editor.ClickAsync("SaveAsync");

		WorkCard created = Assert.Single(await TestData.ReadLaneAsync(database, CardLane.Queue));
		Assert.Equal(worker.Id, created.WorkerAgentId);
		Assert.Equal(4, created.AttemptLimit);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Delete_AsksForConfirmationAndDeletesNothingUntilConfirmed()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		await dialog.Editor.ClickAsync("BeginConfirmation");

		string html = await dialog.Editor.HtmlAsync();
		Assert.Contains("Delete this card?", html, StringComparison.Ordinal);
		Assert.NotNull(await TestData.FindCardAsync(database, card.Id));

		await dialog.Editor.ClickAsync("DeleteAsync");

		Assert.Null(await TestData.FindCardAsync(database, card.Id));
		Assert.Equal(1, dialog.Changes);
	}

	private static async Task<WorkCard> AddStoppedCardAsync(TestDatabase database, RoundKind? resumeRound = null)
	{
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", state: CardState.Error, attempt: 1, resumeRound: resumeRound);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Blocked, "Claude", StopReason);

		return card;
	}

	private static async Task<WorkCard> ReadAsync(TestDatabase database, int id)
	{
		return await TestData.FindCardAsync(database, id) ?? throw new InvalidOperationException($"Card {id} is gone.");
	}
}
