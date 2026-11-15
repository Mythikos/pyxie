using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components;

using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The footer of the editor dialogs: Cancel and Save, a Delete only for an item that exists, and a confirmation that does not outlive the dialog.</summary>
public sealed class EditorFooterTests
{
	[Fact]
	public async Task Render_OffersCancelAndSaveForANewItem()
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<EditorFooter> footer = await harness.RenderAsync<EditorFooter>(Parameters(visible: true, deletable: false));

		Assert.Equal(["Cancel", "Save"], Buttons(await footer.HtmlAsync()));
	}

	[Fact]
	public async Task Render_OffersDeleteBeforeCancelAndSaveForAnItemThatExists()
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<EditorFooter> footer = await harness.RenderAsync<EditorFooter>(Parameters(visible: true, deletable: true));

		Assert.Equal(["Delete", "Cancel", "Save"], Buttons(await footer.HtmlAsync()));
	}

	[Fact]
	public async Task Delete_ReplacesCancelAndSaveWithConfirmationNamedAfterTheItemAndCancelingBringsThemBack()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<EditorFooter> footer = await harness.RenderAsync<EditorFooter>(Parameters(visible: true, deletable: true));

		await footer.ClickAsync("BeginDelete");

		Assert.Equal(["Cancel", "Confirm"], Buttons(await footer.HtmlAsync()));
		Assert.Matches(@"aria-label=""Cancel deleting Claude""", await footer.HtmlAsync());
		Assert.Matches(@"aria-label=""Confirm deleting Claude""", await footer.HtmlAsync());

		await footer.ClickAsync("CancelDelete");

		Assert.Equal(["Delete", "Cancel", "Save"], Buttons(await footer.HtmlAsync()));
	}

	[Fact]
	public async Task ShowingTheDialogAgain_StartsWithoutAPendingConfirmation()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<EditorFooter> footer = await harness.RenderAsync<EditorFooter>(Parameters(visible: true, deletable: true));
		await footer.ClickAsync("BeginDelete");
		Assert.Equal(["Cancel", "Confirm"], Buttons(await footer.HtmlAsync()));

		await footer.RunAsync(instance => instance.SetParametersAsync(ViewOf(visible: false, deletable: true)));
		await footer.RunAsync(instance => instance.SetParametersAsync(ViewOf(visible: true, deletable: true)));

		Assert.Equal(["Delete", "Cancel", "Save"], Buttons(await footer.HtmlAsync()));
	}

	[Fact]
	public async Task Busy_DisablesEveryButton()
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<EditorFooter> footer = await harness.RenderAsync<EditorFooter>(new Dictionary<string, object> { ["Visible"] = true, ["Deletable"] = true, ["Busy"] = true, ["Subject"] = "Claude" });

		string html = await footer.HtmlAsync();
		MatchCollection buttons = Regex.Matches(html, @"<button\b[^>]*>");
		Assert.Equal(3, buttons.Count);
		Assert.All(buttons, button => Assert.Contains("disabled", button.Value, StringComparison.Ordinal));
	}

	private static Dictionary<string, object> Parameters(bool visible, bool deletable)
	{
		return new Dictionary<string, object> { ["Visible"] = visible, ["Deletable"] = deletable, ["Subject"] = "Claude" };
	}

	private static ParameterView ViewOf(bool visible, bool deletable)
	{
		return ParameterView.FromDictionary(new Dictionary<string, object?> { ["Visible"] = visible, ["Deletable"] = deletable, ["Subject"] = "Claude" });
	}

	private static List<string> Buttons(string html)
	{
		return [.. Regex.Matches(html, @"<button\b[^>]*>(.*?)</button>", RegexOptions.Singleline).Select(match => Regex.Replace(match.Groups[1].Value, "<[^>]*>", string.Empty).Trim())];
	}
}
