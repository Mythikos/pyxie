using System.Net;
using System.Text.RegularExpressions;

using Microsoft.JSInterop;

using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryHeaderHubPathTests
{
	private const string HubPath = "C:\\pyxie work\\app\\repository";
	private const string ClipboardWrite = "navigator.clipboard.writeText";

	[Fact]
	public async Task Render_ShowsTheHubsPathAloneInACodeBoxLabelledHubAfterTheOriginAndNowhereElse()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);

		string html = WebUtility.HtmlDecode(await host.Header.HtmlAsync());

		int facts = html.IndexOf("repo-facts", StringComparison.Ordinal);
		int origin = html.IndexOf(">Origin<", StringComparison.Ordinal);
		int hub = html.IndexOf(">Hub<", StringComparison.Ordinal);
		int path = html.IndexOf($">{HubPath}</code>", StringComparison.Ordinal);
		Assert.True(facts >= 0 && facts < origin && origin < hub && hub < path, $"The hub's path should be the last fact, after the origin, in a code box:{Environment.NewLine}{html}");
		Assert.Single(Regex.Matches(html, Regex.Escape(HubPath), RegexOptions.None, TimeSpan.FromSeconds(5)));
		Assert.DoesNotContain("git clone", html, StringComparison.Ordinal);
		Assert.Matches(ButtonText("Copy"), html);
		Assert.Empty(host.Browser.Calls);
	}

	[Fact]
	public async Task Render_KeepsTheCodeBoxReachableByKeyboardBecauseALongPathScrollsInIt()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);

		string html = await host.Header.HtmlAsync();

		Assert.Matches(new Regex("<code class=\"repo-path\"[^>]*tabindex=\"0\"", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
	}

	[Fact]
	public async Task Copy_WritesTheHubsPathAloneToTheClipboardAndSaysCopiedUntilTheTimerRunsOut()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);

		await host.Header.ClickAsync("CopyAsync");

		(string Identifier, object?[] Arguments) call = Assert.Single(host.Browser.Calls, recorded => recorded.Identifier == ClipboardWrite);
		Assert.Equal([HubPath], call.Arguments);
		Assert.Matches(ButtonText("Copied"), await host.Header.HtmlAsync());
		Assert.Equal(1, host.Clock.PendingTimers);

		await host.Header.RunAsync(_ => host.Clock.FireTimers());

		string html = await host.Header.HtmlAsync();
		Assert.Matches(ButtonText("Copy"), html);
		Assert.DoesNotContain("Copied", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Copy_KeepsTheButtonsWidthWhileItSaysCopiedSoTheRowDoesNotMove()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);

		string before = ButtonTag(await host.Header.HtmlAsync());
		await host.Header.ClickAsync("CopyAsync");
		string after = ButtonTag(await host.Header.HtmlAsync());

		Assert.Contains("min-width: 92px", before, StringComparison.Ordinal);
		Assert.Equal(before, after);
	}

	[Fact]
	public async Task Copy_WhenTheBrowserHasNoClipboard_ChangesNothingAndSaysNothing()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);
		host.Browser.Failure = new JSException("Cannot read properties of undefined (reading 'writeText')");

		await host.Header.ClickAsync("CopyAsync");

		string html = await host.Header.HtmlAsync();
		Assert.Single(host.Browser.Calls, recorded => recorded.Identifier == ClipboardWrite);
		Assert.Matches(ButtonText("Copy"), html);
		Assert.DoesNotContain("Copied", html, StringComparison.Ordinal);
		Assert.DoesNotContain("alert", html, StringComparison.Ordinal);
		Assert.Equal(0, host.Clock.PendingTimers);
	}

	[Fact]
	public async Task Dispose_StopsTheTimerOfACopyThatIsStillShown()
	{
		RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(HubPath);
		await host.Header.ClickAsync("CopyAsync");
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
		Assert.True(button.Success, $"The header should have a Copy button:{Environment.NewLine}{html}");

		return button.Value;
	}
}
