using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board.Models;
using Pyxie.Web.Components.Pages;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The card dialog does what it was asked once. A second click the person made while the app was busy with the first is handled once the first click's action has ended, and the closed dialog keeps its buttons, enabled again. A dialog that has done its work does nothing more until it is opened again.</summary>
public sealed class CardEditorDoubleSubmitTests
{
	[Fact]
	public async Task Save_PressedAgainBeforeTheBrowserShowsTheDialogClosed_CreatesOneCard()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: false);
		await using ServiceProvider services = BoardServices.Create(database, new GateStubJsRuntime());
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<Board>();
		await host.ClickAsync(await host.FindButtonAsync("Add"));
		await host.TypeAsync(await host.FindFieldAsync("_form.Name"), "Fix the login");
		await host.TypeAsync(Assert.Single(await host.FindElementsAsync("textarea", field => field.Attribute("name") == "_form.Description")), "Users cannot sign in.");
		await host.ClickAsync(await host.FindOptionAsync("Feature"));
		await host.ClickAsync(await host.FindOptionAsync("Claude"));
		GateRenderedElement save = await host.FindButtonAsync("Save");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(save);
		await host.ClickAsync(save);
		await host.AcknowledgeAsync();

		Assert.Single(await TestData.ReadLaneAsync(database, CardLane.Queue));
		Assert.False(await host.IsModalOpenAsync(), "The saved card should have closed the dialog.");
	}

	[Fact]
	public async Task Save_PressedAgainWhileTheFirstSaveRuns_CreatesOneCard()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.EnsureDefaultAgentAsync(database);
		GatedCardService gated = new(new CardService(database.ContextFactory, database.Clock));
		gated.Creates.Hold(1);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, cardService: gated);
		Fill(dialog.Form(), worker);

		Task first = dialog.Editor.ClickAsync("SaveAsync");
		await gated.Creates.WaitForStartAsync(1);
		await dialog.Editor.ClickAsync("SaveAsync");
		gated.Creates.Release(1);
		await first;

		Assert.Equal(1, gated.Creates.Started);
		Assert.Single(await TestData.ReadLaneAsync(database, CardLane.Queue));
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Save_OfAnEditPressedAgainAfterItSaved_WritesTheCardOnce()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().Description = "Spell out which sign-in fails.";

		await dialog.Editor.ClickAsync("SaveAsync");
		await dialog.Editor.ClickAsync("SaveAsync");

		WorkCard stored = await ReadAsync(database, card.Id);
		Assert.Equal("Spell out which sign-in fails.", stored.Description);
		Assert.Equal(card.Version + 1, stored.Version);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Save_PressedAgainAfterTheModalReportedItsOpeningLate_WritesTheCardOnce()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().Description = "Spell out which sign-in fails.";
		await dialog.Editor.ClickAsync("SaveAsync");

		// The modal reports that it opened once its 150 ms animation has ended, which a save can beat.
		await dialog.Editor.ClickAsync("OnModalVisibleChanged", true);
		await dialog.Editor.ClickAsync("SaveAsync");

		Assert.Equal(card.Version + 1, (await ReadAsync(database, card.Id)).Version);
		Assert.Equal(1, dialog.Changes);
	}

	[Fact]
	public async Task Save_AfterThePersonClosedTheModal_SavesNothing()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.EnsureDefaultAgentAsync(database);
		await using CardDialog dialog = await CardDialog.OpenAsync(database);
		Fill(dialog.Form(), worker);

		// Escape or a click on the backdrop closes the modal, which reports it.
		await dialog.Editor.ClickAsync("OnModalVisibleChanged", false);
		await dialog.Editor.ClickAsync("SaveAsync");

		Assert.Empty(await TestData.ReadLaneAsync(database, CardLane.Queue));
		Assert.Equal(0, dialog.Changes);
	}

	[Fact]
	public async Task Retry_PressedAgainAfterTheCardWasRetried_RetriesOnce()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", state: CardState.Error, attempt: 1);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);

		await dialog.Editor.ClickAsync("RetryAsync");
		await dialog.Editor.ClickAsync("RetryAsync");

		Assert.Equal(CardState.Ready, (await ReadAsync(database, card.Id)).State);
		Assert.Equal(1, dialog.Changes);
		Assert.DoesNotContain("field-error", await dialog.Editor.HtmlAsync(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Delete_ConfirmedAgainAfterTheCardWasDeleted_DeletesOnce()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		await dialog.Editor.ClickAsync("BeginConfirmation");

		await dialog.Editor.ClickAsync("DeleteAsync");
		await dialog.Editor.ClickAsync("DeleteAsync");

		Assert.Null(await TestData.FindCardAsync(database, card.Id));
		Assert.Equal(1, dialog.Changes);
		Assert.DoesNotContain("field-error", await dialog.Editor.HtmlAsync(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task CloseCard_ConfirmedAgainAfterTheCardWasClosed_ClosesOnce()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Fix the login", CardLane.Complete);
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		await dialog.Editor.ClickAsync("BeginConfirmation");

		await dialog.Editor.ClickAsync("CloseCardAsync");
		await dialog.Editor.ClickAsync("CloseCardAsync");

		Assert.Equal(CardLane.Closed, (await ReadAsync(database, card.Id)).Lane);
		Assert.Equal(1, dialog.Changes);
		Assert.DoesNotContain("field-error", await dialog.Editor.HtmlAsync(), StringComparison.Ordinal);
	}

	private static void Fill(CardFormModel form, Agent worker)
	{
		form.Name = "Fix the login";
		form.Description = "Users cannot sign in.";
		form.CategoryId = PyxieSeedData.FeatureCategoryId;
		form.WorkerAgentId = worker.Id;
	}

	private static async Task<WorkCard> ReadAsync(TestDatabase database, int id)
	{
		return await TestData.FindCardAsync(database, id) ?? throw new InvalidOperationException($"Card {id} is gone.");
	}
}
