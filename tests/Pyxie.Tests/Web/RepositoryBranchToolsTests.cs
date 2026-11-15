using System.Text.RegularExpressions;

using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Repository;
using Pyxie.Web.Components.Repository.Navigation;

using Xunit;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Tests.Web;

public sealed class RepositoryBranchToolsTests
{
	[Theory]
	[InlineData("main", "main")]
	[InlineData("feature/x", "feature%2Fx")]
	[InlineData("fix&more#1", "fix%26more%231")]
	public async Task Render_OffersTheSelectedBranchAsAZipDownloadWithItsNameEscaped(string branch, string escaped)
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<BranchTools> tools = await RenderAsync(harness, branch, "main", branch);

		string html = await tools.HtmlAsync();
		Match link = Regex.Match(html, "<a\\b[^>]*href=\"repository/archive\\?[^\"]*\"[^>]*>.*?</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(5));
		Assert.True(link.Success, $"The branch tools must link to the archive endpoint: {html}");
		Assert.Contains($"href=\"repository/archive?branch={escaped}\"", link.Value, StringComparison.Ordinal);
		Assert.Matches("\\sdownload[\\s=>]", link.Value);
		Assert.Matches(">Zip</a>", link.Value);
		Assert.Contains("anticon download", link.Value, StringComparison.Ordinal);
		Assert.Contains("title=\"Download the branch as a zip file\"", link.Value, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_LabelsTheBranchSelectWithVisibleTextThatNamesItsTextField()
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<BranchTools> tools = await RenderAsync(harness, "main", "main", "feature/x", "fix");

		string html = await tools.HtmlAsync();
		Match label = Regex.Match(html, "<label id=\"(?<id>[^\"]+)\" class=\"branch-label\"[^>]*>Branch</label>", RegexOptions.None, TimeSpan.FromSeconds(5));
		Assert.True(label.Success, $"The select should have a visible label:{Environment.NewLine}{html}");
		Assert.Matches(new Regex($"<input\\b[^>]*role=\"combobox\"[^>]*aria-labelledby=\"{Regex.Escape(label.Groups["id"].Value)}\"", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
	}

	[Fact]
	public async Task Render_OffersEveryBranchWithTheTargetBranchFirst()
	{
		await using ComponentHarness harness = new(_ => { });

		RenderedComponent<BranchTools> tools = await RenderAsync(harness, "fix", "main", "feature/x", "fix", "main");

		string html = await tools.HtmlAsync();
		IReadOnlyList<string> options = [.. Regex.Matches(html, "<div class=\"ant-select-item-option-content\">([^<]*)</div>", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(match => match.Groups[1].Value)];
		Assert.Equal(["main", "feature/x", "fix"], options);
	}

	// The branch tools of a repository with branches, showing one of them. The first branch is the one named.
	private static Task<RenderedComponent<BranchTools>> RenderAsync(ComponentHarness harness, string shown, string target, params string[] branches)
	{
		RepositoryEntity repository = new() { Path = "hub", Origin = "origin", TargetBranch = target };
		IReadOnlyList<GitBranch> all = [.. (branches.Length == 0 ? [shown] : branches).Select(name => new GitBranch(name, "0123456789abcdef", DateTime.UnixEpoch, "Subject"))];
		RepositoryOverview overview = new(repository, "app", all);

		return harness.RenderAsync<BranchTools>(new Dictionary<string, object>
		{
			[nameof(BranchTools.Overview)] = overview,
			[nameof(BranchTools.Location)] = new RepositoryLocation(RepositoryTab.Files, shown, string.Empty, string.Empty, 0),
		});
	}
}
