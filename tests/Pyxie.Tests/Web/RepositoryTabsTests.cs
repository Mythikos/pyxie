using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Repository;
using Pyxie.Web.Components.Repository.Navigation;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryTabsTests
{
	[Fact]
	public async Task Render_OffersFilesHistoryAndBranchesAsLinksInATablistWithTheCurrentOneSelected()
	{
		await using ComponentHarness harness = new(_ => { });

		string html = await RenderAsync(harness, new RepositoryLocation(RepositoryTab.History, string.Empty, string.Empty, string.Empty, 0), 7);

		Assert.Matches(new Regex("<div class=\"repository-tabs\"[^>]*role=\"tablist\"[^>]*aria-label=\"Repository views\"", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
		IReadOnlyList<Match> tabs = Tabs(html);
		Assert.Equal(["Files", "History", "Branches7"], tabs.Select(Text));
		Assert.Equal(["false", "true", "false"], tabs.Select(tab => Regex.Match(tab.Value, "aria-selected=\"(true|false)\"", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value));
		Assert.Equal(["repository-tab", "repository-tab repository-tab-selected", "repository-tab"], tabs.Select(tab => Regex.Match(tab.Value, "class=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value));
	}

	[Fact]
	public async Task Render_ShowsTheBranchCountOnTheBranchesTabAlone()
	{
		await using ComponentHarness harness = new(_ => { });

		string html = await RenderAsync(harness, new RepositoryLocation(RepositoryTab.Files, string.Empty, string.Empty, string.Empty, 0), 12);

		Assert.Single(Regex.Matches(html, "<span class=\"pyx-chip repository-count\"[^>]*>12</span>", RegexOptions.None, TimeSpan.FromSeconds(5)));
		Assert.Single(Regex.Matches(html, "pyx-chip", RegexOptions.None, TimeSpan.FromSeconds(5)));
	}

	[Fact]
	public async Task Render_LinksTheTabsThatAreNotShownToTheTopOfTheirViewKeepingTheBranch()
	{
		await using ComponentHarness harness = new(_ => { });
		RepositoryLocation location = new(RepositoryTab.Files, "feature/x", "docs/design", string.Empty, 0);

		string html = await RenderAsync(harness, location, 3);

		IReadOnlyList<string> links = Links(html);
		Assert.Equal("http://localhost/?tab=history&branch=feature%2Fx", links[1]);
		Assert.Equal("http://localhost/?tab=branches&branch=feature%2Fx", links[2]);
	}

	[Fact]
	public async Task Render_LinksTheTabThatIsShownToWhereThePageIsSoChoosingItAgainChangesNothing()
	{
		await using ComponentHarness harness = new(_ => { });

		string files = await RenderAsync(harness, new RepositoryLocation(RepositoryTab.Files, "feature/x", "docs/design", string.Empty, 0), 3);
		string history = await RenderAsync(harness, new RepositoryLocation(RepositoryTab.History, "feature/x", string.Empty, "0123abcd", 2), 3);

		Assert.Equal("http://localhost/?branch=feature%2Fx&path=docs%2Fdesign", Links(files)[0]);
		Assert.Equal("http://localhost/?tab=history&branch=feature%2Fx&commit=0123abcd&page=2", Links(history)[1]);
	}

	// The opening tag and the content of each tab, in order.
	private static IReadOnlyList<Match> Tabs(string html)
	{
		return [.. Regex.Matches(html, "<a [^>]*role=\"tab\"[^>]*>(.*?)</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(5)).Cast<Match>()];
	}

	// The address each tab links to, in order.
	private static IReadOnlyList<string> Links(string html)
	{
		return [.. Tabs(html).Select(tab => WebUtility.HtmlDecode(Regex.Match(tab.Value, "href=\"([^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value))];
	}

	// The text of a tab, with the count chip appended and without the whitespace the markup is indented with.
	private static string Text(Match tab)
	{
		return Regex.Replace(Regex.Replace(tab.Groups[1].Value, "<[^>]+>", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5)), "\\s+", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5));
	}

	private static async Task<string> RenderAsync(ComponentHarness harness, RepositoryLocation location, int branchCount)
	{
		RenderedComponent<RepositoryTabs> tabs = await harness.RenderAsync<RepositoryTabs>(new Dictionary<string, object>
		{
			[nameof(RepositoryTabs.Location)] = location,
			[nameof(RepositoryTabs.BranchCount)] = branchCount,
		});

		return await tabs.HtmlAsync();
	}
}
