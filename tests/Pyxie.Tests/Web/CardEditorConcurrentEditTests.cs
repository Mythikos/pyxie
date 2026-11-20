using Pyxie.Data.Entities;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardEditorConcurrentEditTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ARefusedQueueActionDoesNotAdoptSomebodyElsesVersionForARepeatedPress(bool delete)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Initial task");
		await using CardDialog dialog = await CardDialog.OpenAsync(database, card.Id);
		dialog.Form().Description = "A local draft";
		if (delete)
		{
			await dialog.Editor.ClickAsync("BeginConfirmation");
		}

		CardFormModel remote = CardFormModel.FromCard(card);
		remote.Description = "The updated instructions";
		WorkCard revised = ResultAssert.Succeeded(await dialog.Cards.UpdateAsync(card.Id, remote.ToInput(), TestContext.Current.CancellationToken));
		string action = delete ? "DeleteAsync" : "SaveAsync";
		await dialog.Editor.ClickAsync(action);
		await dialog.Editor.ClickAsync(action);

		WorkCard stored = Assert.IsType<WorkCard>(await TestData.FindCardAsync(database, card.Id));
		Assert.Equal((revised.Version, remote.Description), (stored.Version, stored.Description));
		Assert.Equal("A local draft", dialog.Form().Description);
		Assert.Equal(1, dialog.Changes);
		Assert.DoesNotContain("Delete this card?", await dialog.Editor.HtmlAsync(), StringComparison.Ordinal);

		await dialog.Editor.ClickAsync("ReloadDraftAsync", dialog.Editor.ReadField<int>("_opening"));
		Assert.Equal(remote.Description, dialog.Form().Description);
		if (delete)
		{
			await dialog.Editor.ClickAsync("BeginConfirmation");
			await dialog.Editor.ClickAsync("DeleteAsync");
			Assert.Null(await TestData.FindCardAsync(database, card.Id));
		}
		else
		{
			dialog.Form().Description = "Revised after review";
			await dialog.Editor.ClickAsync("SaveAsync");
			WorkCard saved = Assert.IsType<WorkCard>(await TestData.FindCardAsync(database, card.Id));
			Assert.Equal((revised.Version + 1, dialog.Form().Description), (saved.Version, saved.Description));
		}
	}
}
