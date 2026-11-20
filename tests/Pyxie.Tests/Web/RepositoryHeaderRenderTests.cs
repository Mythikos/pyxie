using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryHeaderRenderTests
{
	private const string ExampleRemote = "https://example.com/owner/app.git";

	[Fact]
	public async Task Render_TitlesTheHeaderWithTheProjectsNameAsTheOnlyHeading()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(projectName: "storefront-api", origin: "https://example.com/owner/other-name.git");

		string html = await host.Header.HtmlAsync();

		Assert.Matches(new Regex("<h1\\b[^>]*>storefront-api</h1>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
		Assert.Single(Regex.Matches(html, "<h[1-6]\\b", RegexOptions.None, TimeSpan.FromSeconds(5)));
		Assert.DoesNotContain("other-name</h1>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheTargetBranchInAChipWithALockedIcon()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(targetBranch: "trunk");

		string html = await host.Header.HtmlAsync();

		Assert.Matches(new Regex("<span class=\"repo-branch\"[^>]*title=\"Target branch\"[^>]*><span class=\"anticon lock\"><svg [^>]*aria-hidden=\"true\".*?</svg></span>trunk</span>", RegexOptions.Singleline, TimeSpan.FromSeconds(5)), html);
	}

	[Fact]
	public async Task Render_ShowsWhereTheProjectWasImportedFromWithoutCredentials()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(origin: "https://someone:secret-token@example.com/owner/app.git");

		string html = await host.Header.HtmlAsync();

		Assert.Contains("example.com/owner/app.git", html, StringComparison.Ordinal);
		Assert.DoesNotContain("secret-token", html, StringComparison.Ordinal);
		Assert.Matches(new Regex("<dd class=\"repo-origin\"[^>]*>\\s*<code[^>]*>https://example.com/owner/app.git</code>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
	}

	[Fact]
	public async Task Render_ClaimsNothingAboutHowFarTheHubIsFromWhereItWasImportedFrom()
	{
		// Pyxie never fetches from where a project came from, and people may pull the hub's work back into it, so no comparison with it would be true.
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync(origin: ExampleRemote);

		string html = await host.Header.HtmlAsync();

		Assert.DoesNotContain("ahead", html, StringComparison.Ordinal);
		Assert.DoesNotContain("behind", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Up to date", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_HasNoWayToCloseTheProjectBecauseItsRowInTheProjectsPanelHasIt()
	{
		await using RepositoryHeaderHost host = await RepositoryHeaderHost.RenderAsync();

		string html = await host.Header.HtmlAsync();

		Assert.DoesNotContain(">Close<", html, StringComparison.Ordinal);
		Assert.Equal(2, Regex.Matches(html, "<button\\b", RegexOptions.None, TimeSpan.FromSeconds(5)).Count);
		Assert.Matches(new Regex(">\\s*Copy\\s*</button>", RegexOptions.None, TimeSpan.FromSeconds(5)), WebUtility.HtmlDecode(html));
	}
}
