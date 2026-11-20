using System.Text.RegularExpressions;

using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Pages;
using Pyxie.Web.Components.Projects;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class SettingsPageTests
{
	private static readonly string[] s_ids = ["agents", "gates", "categories", "sandbox", "global"];

	[Fact]
	public async Task Page_SaysNoProjectIsOpenAndLinksToTheRepositoryPageWhileNoneIs()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ProjectPageHost host = new(database);

		string html = await (await host.RenderAsync<Settings>()).HtmlAsync();

		Assert.Contains(NoProjectOpen.Text, html, StringComparison.Ordinal);
		Assert.Contains("href=\"repository\"", html, StringComparison.Ordinal);
		Assert.Equal([("settings#global", "Global")], Links(html));
		Assert.Equal(["Global"], CurrentLinks(html));
		Assert.Empty(GateHtml.TextOfElementsWithClass(html, "gate-text"));
		Assert.Equal(0, GateHtml.CountIconButtons(html, "Add gate"));
		Assert.DoesNotContain("Dockerfile", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Page_ShowsTheProjectsSettingsOnceAProjectIsOpenedAndTheEmptyStateOnceItIsClosed()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await page.WaitForHtmlAsync(html => html.Contains("Feature", StringComparison.Ordinal) && GateHtml.TextOfElementsWithClass(html, "gate-text").Contains("Build"), "The page should show the open project's categories and gates");

		Assert.Contains("Settings sections", open, StringComparison.Ordinal);
		Assert.DoesNotContain(NoProjectOpen.Text, open, StringComparison.Ordinal);

		host.CloseAndAnnounce();
		string closed = await page.WaitForHtmlAsync(html => html.Contains(NoProjectOpen.Text, StringComparison.Ordinal), "The page should say no project is open once it was closed");

		Assert.DoesNotContain("Feature", closed, StringComparison.Ordinal);
		Assert.Empty(GateHtml.TextOfElementsWithClass(closed, "gate-text"));
		Assert.Equal(["Global"], CurrentLinks(closed));
	}

	[Fact]
	public async Task Page_ShowsProjectSectionsAndGlobalSettingsInOrder()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		List<string> headings = [.. Regex.Matches(open, @"<h2\b[^>]*>(.*?)</h2>").Select(match => match.Groups[1].Value)];
		List<string> ids = [.. Regex.Matches(open, @"<section id=""([^""]+)""").Select(match => match.Groups[1].Value)];
		Assert.Equal(["Agents", "Gates", "Categories", "Sandbox", "Global"], headings);
		Assert.Equal(s_ids, ids);
		Assert.Equal(["Build", "Tests"], GateHtml.TextOfElementsWithClass(open, "gate-text"));
		Assert.Equal(1, GateHtml.CountIconButtons(open, "Add gate"));
		Assert.Equal(1, GateHtml.CountIconButtons(open, "Add agent"));
	}

	[Fact]
	public async Task Page_GivesEachPanelAHeadingItIsNamedBy()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		foreach (string id in s_ids)
		{
			Assert.Matches($@"<section id=""{id}"" class=""pyx-panel[^""]*"" aria-labelledby=""{id}-heading""", open);
			Assert.Matches($@"<h2 id=""{id}-heading""", open);
		}
	}

	[Fact]
	public async Task Page_ShowsANavWithALinkToEachPanelThatNamesItsFragmentOnThePageItself()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		Assert.Matches(@"<nav class=""settings-nav""[^>]*aria-label=""Settings sections""", open);
		Assert.Equal([("settings#agents", "Agents"), ("settings#gates", "Gates"), ("settings#categories", "Categories"), ("settings#sandbox", "Sandbox"), ("settings#global", "Global")], Links(open));
	}

	[Fact]
	public async Task Page_MarksAgentsAsTheCurrentSectionWhileTheUrlNamesNoOther()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		Assert.Equal(["Agents"], CurrentLinks(open));
	}

	[Fact]
	public async Task Page_MarksTheSectionTheUrlsFragmentNamesAtFirst()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		host.Navigation.MoveTo("settings#categories");
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		Assert.Equal(["Categories"], CurrentLinks(open));
	}

	[Theory]
	[InlineData("settings#gates", "Gates")]
	[InlineData("settings#sandbox", "Sandbox")]
	[InlineData("settings#categories", "Categories")]
	[InlineData("settings#agents", "Agents")]
	[InlineData("settings#nowhere", "Agents")]
	[InlineData("settings", "Agents")]
	public async Task Page_FollowsTheLocationAsNavLinksAreFollowed(string location, string expectedCurrent)
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();
		host.OpenAndAnnounce();
		await OpenPageAsync(page);
		host.Navigation.MoveTo("settings#gates");
		await page.WaitForHtmlAsync(html => CurrentLinks(html).SequenceEqual(["Gates"]), "The page should follow the first move");

		host.Navigation.MoveTo(location);

		string after = await page.WaitForHtmlAsync(html => CurrentLinks(html).SequenceEqual([expectedCurrent]), $"The page should mark {expectedCurrent} as current");
		Assert.Single(CurrentLinks(after));
	}

	[Fact]
	public async Task Page_MarksALinkAsCurrentWhenItIsClickedBecauseTheBrowserFollowsAFragmentLinkWithoutTellingBlazor()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database, new GateStubJsRuntime());
		host.OpenAndAnnounce();
		await using GateInteractiveHost page = new(host.Services);
		await page.StartAsync<Settings>();
		Assert.Equal(["Agents"], await CurrentLinksAsync(page));

		await page.ClickAsync(await LinkAsync(page, "Gates"));
		Assert.Equal(["Gates"], await CurrentLinksAsync(page));

		await page.ClickAsync(await LinkAsync(page, "Sandbox"));
		Assert.Equal(["Sandbox"], await CurrentLinksAsync(page));

		await page.ClickAsync(await LinkAsync(page, "Agents"));
		Assert.Equal(["Agents"], await CurrentLinksAsync(page));
	}

	[Fact]
	public async Task Page_StopsFollowingTheLocationOnceItIsDisposed()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();
		host.OpenAndAnnounce();
		string before = await OpenPageAsync(page);

		page.Instance.Dispose();
		host.Navigation.MoveTo("settings#sandbox");
		await Task.Yield();

		Assert.Equal(CurrentLinks(before), CurrentLinks(await page.HtmlAsync()));
	}

	[Fact]
	public async Task Page_ShowsTheSandboxPanelWithTheProjectsDockerfileAndTheSignInsOnceAProjectIsOpened()
	{
		await using TestDatabase database = new();
		await using ProjectPageHost host = new(database);
		RenderedComponent<Settings> page = await host.RenderAsync<Settings>();

		host.OpenAndAnnounce();
		string open = await OpenPageAsync(page);

		Assert.Contains("FROM ${PYXIE_BASE}", open, StringComparison.Ordinal);
		Assert.Matches(@"<h3 class=""sandbox-side-title""[^>]*>Sign-ins</h3>", open);
		Assert.Equal([("Claude Code account", "Not found"), ("Claude Code API key", "Not found"), ("Codex account", "Not found"), ("Codex API key", "Not found"), ("DeepSeek account", "Not found"), ("DeepSeek API key", "Not found")], SandboxEditorHost.ReadSignIns(open).Select(row => (row.Name, row.Status)));
		Assert.Matches(@"<button\b[^>]*sandbox-save[^>]*>Save</button>", open);
	}

	// The page once it shows the open project's panels.
	private static Task<string> OpenPageAsync(RenderedComponent<Settings> page)
	{
		return page.WaitForHtmlAsync(html => html.Contains("Dockerfile", StringComparison.Ordinal) && html.Contains("Feature", StringComparison.Ordinal), "The page should show the open project's settings");
	}

	private static List<(string Href, string Text)> Links(string html)
	{
		string nav = Regex.Match(html, @"<nav class=""settings-nav"".*?</nav>", RegexOptions.Singleline).Value;

		return [.. Regex.Matches(nav, @"<a href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.Singleline).Select(match => (match.Groups[1].Value, match.Groups[2].Value))];
	}

	// The text of the nav links that are marked as the current section.
	private static List<string> CurrentLinks(string html)
	{
		string nav = Regex.Match(html, @"<nav class=""settings-nav"".*?</nav>", RegexOptions.Singleline).Value;

		return [.. Regex.Matches(nav, @"<a href=""[^""]*""[^>]*aria-current=""true""[^>]*>(.*?)</a>", RegexOptions.Singleline).Select(match => match.Groups[1].Value)];
	}

	private static async Task<GateRenderedElement> LinkAsync(GateInteractiveHost page, string title)
	{
		return Assert.Single(await page.FindElementsAsync("a", link => link.Text == title));
	}

	private static async Task<List<string>> CurrentLinksAsync(GateInteractiveHost page)
	{
		return [.. (await page.FindElementsAsync("a", link => link.Attribute("aria-current") == "true")).Select(link => link.Text)];
	}
}
