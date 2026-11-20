using System.Text.RegularExpressions;

using Microsoft.JSInterop;

using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryCopyButtonTests
{
	private const string HubPath = "C:\\pyxie work\\app\\repository";
	private const string ClipboardWrite = "navigator.clipboard.writeText";

	[Fact]
	public async Task Copy_WritesTheLocationAloneToTheClipboardAndSaysCopiedUntilTheTimerRunsOut()
	{
		await using RepositoryCopyButtonHost host = await RepositoryCopyButtonHost.RenderAsync(HubPath);

		await host.Button.ClickAsync("CopyAsync");

		(string Identifier, object?[] Arguments) call = Assert.Single(host.Browser.Calls, recorded => recorded.Identifier == ClipboardWrite);
		Assert.Equal([HubPath], call.Arguments);
		Assert.Matches(ButtonText("Copied"), await host.Button.HtmlAsync());
		Assert.Equal(1, host.Clock.PendingTimers);

		await host.Button.RunAsync(_ => host.Clock.FireTimers());

		string html = await host.Button.HtmlAsync();
		Assert.Matches(ButtonText("Copy"), html);
		Assert.DoesNotContain("Copied", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Copy_KeepsTheButtonsWidthWhileItSaysCopiedSoTheRowDoesNotMove()
	{
		await using RepositoryCopyButtonHost host = await RepositoryCopyButtonHost.RenderAsync(HubPath);

		string before = ButtonTag(await host.Button.HtmlAsync());
		await host.Button.ClickAsync("CopyAsync");
		string after = ButtonTag(await host.Button.HtmlAsync());

		Assert.Contains("min-width: 92px", before, StringComparison.Ordinal);
		Assert.Equal(before, after);
	}

	[Fact]
	public async Task Copy_WhenTheBrowserHasNoClipboard_ChangesNothingAndSaysNothing()
	{
		await using RepositoryCopyButtonHost host = await RepositoryCopyButtonHost.RenderAsync(HubPath);
		host.Browser.Failure = new JSException("Cannot read properties of undefined (reading 'writeText')");

		await host.Button.ClickAsync("CopyAsync");

		string html = await host.Button.HtmlAsync();
		Assert.Single(host.Browser.Calls, recorded => recorded.Identifier == ClipboardWrite);
		Assert.Matches(ButtonText("Copy"), html);
		Assert.DoesNotContain("Copied", html, StringComparison.Ordinal);
		Assert.DoesNotContain("alert", html, StringComparison.Ordinal);
		Assert.Equal(0, host.Clock.PendingTimers);
	}

	[Fact]
	public async Task Dispose_StopsTheTimerOfACopyThatIsStillShown()
	{
		RepositoryCopyButtonHost host = await RepositoryCopyButtonHost.RenderAsync(HubPath);
		await host.Button.ClickAsync("CopyAsync");
		Assert.Equal(1, host.Clock.PendingTimers);

		await host.DisposeAsync();

		Assert.Equal(0, host.Clock.PendingTimers);
	}

	// The text of a button, whatever whitespace and icon the component puts around it.
	private static Regex ButtonText(string text)
	{
		return new Regex($">\\s*{Regex.Escape(text)}\\s*<", RegexOptions.None, TimeSpan.FromSeconds(5));
	}

	// The opening tag of the Copy button, which carries its width.
	private static string ButtonTag(string html)
	{
		Match button = Regex.Match(html, "<button\\b[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(5));
		Assert.True(button.Success, $"The location should have a Copy button:{Environment.NewLine}{html}");

		return button.Value;
	}
}
