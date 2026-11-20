using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryHeaderHubPathTests
{
	private const string HubPath = "C:\\pyxie work\\app\\repository";

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

	// The text of a button, whatever whitespace and icon the component puts around it.
	private static Regex ButtonText(string text)
	{
		return new Regex($">\\s*{Regex.Escape(text)}\\s*<", RegexOptions.None, TimeSpan.FromSeconds(5));
	}
}
