using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryPageTests
{
	private const string ProjectsPanel = "aria-label=\"Projects\"";
	private const string RepositoryColumn = "aria-label=\"Repository\"";
	private const string ImportField = "import-source-entry";
	private const string ProjectOpenText = "A project is open. Close it to import another.";

	[Fact]
	public async Task Render_ShowsTheProjectsPanelAloneWithTheImportFormWhileThereAreNoProjects()
	{
		await using RepositoryPage page = await RepositoryPage.RenderAsync();

		string html = await page.Page.HtmlAsync();

		Assert.Contains(ProjectsPanel, html, StringComparison.Ordinal);
		Assert.Matches(Element("h2", "Projects"), html);
		Assert.Contains(ImportField, html, StringComparison.Ordinal);
		Assert.Matches(EnabledImportField(), html);
		Assert.DoesNotContain("project-list", html, StringComparison.Ordinal);
		Assert.DoesNotContain(RepositoryColumn, html, StringComparison.Ordinal);
		Assert.DoesNotContain("<h1", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ListsTheProjectsBelowTheImportFormWhileNoneIsOpen()
	{
		await using RepositoryPage page = await RepositoryPage.RenderAsync(async rig =>
		{
			await StoredProjects.AddAsync(rig, "web");
			await StoredProjects.AddAsync(rig, "app");
		});

		string html = await page.Page.HtmlAsync();

		int panel = html.IndexOf(ProjectsPanel, StringComparison.Ordinal);
		int import = html.IndexOf(ImportField, StringComparison.Ordinal);
		int app = html.IndexOf(">app<", StringComparison.Ordinal);
		int web = html.IndexOf(">web<", StringComparison.Ordinal);
		Assert.True(panel >= 0 && import > panel && app > import && web > app, $"The import form comes before the projects, by name. Positions: {panel}, {import}, {app}, {web}");
		Assert.Contains("aria-label=\"Open app\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Open web\"", html, StringComparison.Ordinal);
		Assert.Contains("https://example.test/app.git", html, StringComparison.Ordinal);
		Assert.Matches(EnabledImportField(), html);
		Assert.DoesNotContain(RepositoryColumn, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ChecksEachProjectsFolderWhenItReadsTheList()
	{
		await using RepositoryPage page = await RepositoryPage.RenderAsync(async rig =>
		{
			await StoredProjects.AddAsync(rig, "app");
			Project moved = await StoredProjects.AddAsync(rig, "moved");
			Folders.Delete(moved.Folder);
		});

		string html = await page.Page.HtmlAsync();

		Assert.Contains("Folder is gone", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Open app\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Open moved\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheOpenProjectsRepositoryBesideThePanelThatListsItAsOpen()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature/x");
		await using RepositoryPage page = await RenderImportedAsync(source);
		Project project = Assert.Single(await page.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken));

		string html = WebUtility.HtmlDecode(await page.Page.HtmlAsync());

		int panel = html.IndexOf(ProjectsPanel, StringComparison.Ordinal);
		int column = html.IndexOf(RepositoryColumn, StringComparison.Ordinal);
		Assert.True(panel >= 0 && column > panel, "The projects panel comes first and stays while a project is open.");
		Assert.Matches(Element("h1", project.Name), html);
		Assert.Contains("project-row-open", html, StringComparison.Ordinal);
		Assert.Matches(Element("span", "Open", "project-open-chip"), html);
		Assert.Contains($"aria-label=\"Close {project.Name}\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain($"aria-label=\"Open {project.Name}\"", html, StringComparison.Ordinal);
		Assert.DoesNotMatch(EnabledImportField(), html);
		Assert.Contains($"title=\"{ProjectOpenText}\"", html, StringComparison.Ordinal);
		Assert.Contains(source.Folder, html, StringComparison.Ordinal);
		Assert.Contains("README.md", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheTabsWithTheBranchCountAndTheBranchAndZipInTheBarOnFilesAndHistoryOnly()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature/x");
		await source.CreateBranchAsync("fix");

		foreach (string? tab in new string?[] { null, "history" })
		{
			await using RepositoryPage page = await RenderImportedAsync(source, tab);
			string html = await page.Page.HtmlAsync();

			Assert.Equal(["Files", "History", "Branches3"], TabLabels(html));
			Assert.Matches(new Regex($"<a [^>]*role=\"tab\"[^>]*aria-selected=\"true\"[^>]*>\\s*{(tab is null ? "Files" : "History")}\\s*</a>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
			Assert.Contains("branch-tools", html, StringComparison.Ordinal);
			Assert.Matches(Element("label", "Branch", "branch-label"), html);
			Assert.Contains("href=\"repository/archive?branch=main\"", html, StringComparison.Ordinal);
		}

		await using RepositoryPage branches = await RenderImportedAsync(source, "branches");
		string branchesHtml = await branches.Page.HtmlAsync();
		Assert.Equal(["Files", "History", "Branches3"], TabLabels(branchesHtml));
		Assert.DoesNotContain("branch-tools", branchesHtml, StringComparison.Ordinal);
		Assert.DoesNotContain("repository/archive", branchesHtml, StringComparison.Ordinal);
		Assert.Contains("feature/x", branchesHtml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_LeavesTheBranchAndZipOutOfTheBarWhileOneCommitIsShown()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		string tip = await source.GetHashAsync();
		await using RepositoryPage page = await RenderImportedAsync(source, "history", tip);

		string html = await page.Page.HtmlAsync();

		Assert.Contains("class=\"commit-head\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("branch-tools", html, StringComparison.Ordinal);
		Assert.DoesNotContain("repository/archive", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsAProjectThatIsOpenButHasNoRepositoryInThePanelWithNothingToBrowse()
	{
		await using RepositoryPage page = await RepositoryPage.RenderAsync(async rig =>
		{
			Project project = await StoredProjects.AddAsync(rig, "empty");
			ResultAssert.Succeeded(await rig.Sessions.OpenAsync(project.Id, TestContext.Current.CancellationToken));
		});

		string html = await page.Page.HtmlAsync();

		Assert.Contains("project-row-open", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Close empty\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain(RepositoryColumn, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsWhyTheRepositoryCannotBeReadAndTheHubInTheHeaderOnceAndTheProjectStillClosable()
	{
		string missingHub = Path.Combine(Path.GetTempPath(), $"pyxie-missing-hub-{Guid.NewGuid():N}");
		await using RepositoryPage page = await RepositoryPage.RenderAsync(async rig => await rig.ConnectAsync(missingHub, RepositorySource.Url, "https://example.test/stand-in.git"));

		string html = WebUtility.HtmlDecode(await page.Page.HtmlAsync());

		Assert.Contains("The repository could not be read.", html, StringComparison.Ordinal);
		Assert.Contains(RepositoryColumn, html, StringComparison.Ordinal);

		// Git's own message names the folder it could not read, which is the hub, so the alert repeats it; the header shows it once.
		Match header = Regex.Match(html, "<div class=\"pyx-panel repo-header\".*?</dl></div>", RegexOptions.Singleline, TimeSpan.FromSeconds(5));
		Assert.True(header.Success, $"The header should still be shown:{Environment.NewLine}{html}");
		Assert.Matches(new Regex($"<code class=\"repo-path\"[^>]*>{Regex.Escape(missingHub)}</code>", RegexOptions.None, TimeSpan.FromSeconds(5)), header.Value);
		Assert.Single(Regex.Matches(header.Value, Regex.Escape(missingHub), RegexOptions.None, TimeSpan.FromSeconds(5)));
		Assert.DoesNotContain("branch-tools", html, StringComparison.Ordinal);
		Assert.Contains($"aria-label=\"Close {RepositoryRig.StandInProjectName}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsAProjectOpenedFromTheListAndListsItAgainOnceClosed()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryPage page = await RepositoryPage.RenderAsync(async rig =>
		{
			ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, TestContext.Current.CancellationToken));
			Project imported = rig.OpenProject.Current ?? throw new InvalidOperationException("The import did not open its project.");
			ResultAssert.Succeeded(await rig.Sessions.CloseAsync(imported.Id, TestContext.Current.CancellationToken));
		});
		Project project = Assert.Single(await page.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken));
		string before = await page.Page.HtmlAsync();
		Assert.Contains($"aria-label=\"Open {project.Name}\"", before, StringComparison.Ordinal);
		Assert.DoesNotContain(RepositoryColumn, before, StringComparison.Ordinal);

		ResultAssert.Succeeded(await page.Rig.Sessions.OpenAsync(project.Id, TestContext.Current.CancellationToken));
		string open = await page.Page.WaitForHtmlAsync(markup => markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should show the project opened from the list");

		Assert.Contains(ProjectsPanel, open, StringComparison.Ordinal);
		Assert.Contains($"aria-label=\"Close {project.Name}\"", open, StringComparison.Ordinal);

		ResultAssert.Succeeded(await page.Rig.Sessions.CloseAsync(project.Id, TestContext.Current.CancellationToken));
		string closed = await page.Page.WaitForHtmlAsync(markup => !markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should take the repository away once the project was closed");

		Assert.Contains(ProjectsPanel, closed, StringComparison.Ordinal);
		Assert.Contains($"aria-label=\"Open {project.Name}\"", closed, StringComparison.Ordinal);
		Assert.Matches(EnabledImportField(), closed);
	}

	[Fact]
	public async Task Close_FromTheProjectsPanelTakesTheRepositoryAwayAndLeavesThePanel()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryPage page = await RenderImportedAsync(source);
		Project project = Assert.Single(await page.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken));
		Assert.Contains(RepositoryColumn, await page.Page.HtmlAsync(), StringComparison.Ordinal);

		ResultAssert.Succeeded(await page.Sessions.CloseAsync(project.Id, TestContext.Current.CancellationToken));
		string html = await page.Page.WaitForHtmlAsync(markup => !markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should take the repository away once the project was closed");

		Assert.Null(page.Rig.OpenProject.Current);
		Assert.Contains($"aria-label=\"Open {project.Name}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheRepositoryAnImportConnectedWhileThePageWasOpenAndClearsTheForm()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryPage page = await RepositoryPage.RenderAsync();

		Assert.True(page.Imports.Imports.TryStart(RepositorySource.Url, source.Folder), "No import should be running yet.");
		await page.Imports.Imports.Running;

		// The form and the page follow the import on their own, one after the other, so the markup is read until both have.
		string html = await page.Page.WaitForHtmlAsync(markup => markup.Contains(RepositoryColumn, StringComparison.Ordinal) && markup.Contains("project-row-open", StringComparison.Ordinal) && !markup.Contains("import-bar", StringComparison.Ordinal) && !ImportFieldTag(markup).Contains(source.Folder, StringComparison.Ordinal), "The page should show the repository the import connected, without the import row, and the form cleared");

		Assert.Contains(ProjectsPanel, html, StringComparison.Ordinal);
		Assert.Null(page.Imports.Imports.Current);
		Assert.DoesNotContain("project-importing", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsARepositoryConnectedElsewhere()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryPage page = await RepositoryPage.RenderAsync();
		await page.Rig.ConnectAsync(source.Folder);

		page.Rig.Activity.NotifyProjectChanged();
		string html = await page.Page.WaitForHtmlAsync(markup => markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should show the repository connected from elsewhere");

		Assert.Contains($"aria-label=\"Close {RepositoryRig.StandInProjectName}\"", html, StringComparison.Ordinal);
		Assert.DoesNotMatch(EnabledImportField(), html);
	}

	[Fact]
	public async Task Render_OffersToImportAgainWhenTheProjectIsClosedElsewhere()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RepositoryPage page = await RepositoryPage.RenderAsync();
		await page.Rig.ConnectAsync(source.Folder);
		Project open = page.Rig.OpenProject.Current ?? throw new InvalidOperationException("Connecting did not open a project.");
		page.Rig.Activity.NotifyProjectChanged();
		await page.Page.WaitForHtmlAsync(markup => markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should show the repository");

		ResultAssert.Succeeded(await page.Rig.Sessions.CloseAsync(open.Id, TestContext.Current.CancellationToken));
		string html = await page.Page.WaitForHtmlAsync(markup => !markup.Contains(RepositoryColumn, StringComparison.Ordinal), "The page should take the repository away once the project was closed");

		Assert.Matches(EnabledImportField(), html);
	}

	[Fact]
	public async Task Render_ListensToProjectChangesOnlyWhileItLives()
	{
		await using RepositoryPage page = await RepositoryPage.RenderAsync();
		Assert.Equal(1, EventSubscribers.Count(page.Rig.Activity, nameof(PipelineActivity.ProjectChanged)));

		await page.Page.RunAsync(instance => instance.Dispose());

		Assert.Equal(0, EventSubscribers.Count(page.Rig.Activity, nameof(PipelineActivity.ProjectChanged)));
	}

	// The page of a project imported from a git repository the test made, which is open.
	private static Task<RepositoryPage> RenderImportedAsync(TempGitRepository source, string? tab = null, string? commit = null)
	{
		return RepositoryPage.RenderAsync(async rig => ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(source.Folder, TestContext.Current.CancellationToken)), tab, commit);
	}

	// An element with a class, or any class when none is given, whose only content is a text, in any order of attributes.
	private static Regex Element(string name, string text, string? cssClass = null)
	{
		string attribute = cssClass is null ? string.Empty : $"[^>]*class=\"[^\"]*\\b{Regex.Escape(cssClass)}\\b[^\"]*\"";

		return new Regex($"<{name}\\b{attribute}[^>]*>\\s*{Regex.Escape(text)}\\s*</{name}>", RegexOptions.None, TimeSpan.FromSeconds(5));
	}

	// The opening tag of the import form's field, whatever state it is in.
	private static string ImportFieldTag(string html)
	{
		return Regex.Match(html, $"<input\\b[^>]*id=\"{ImportField}\"[^>]*>", RegexOptions.None, TimeSpan.FromSeconds(5)).Value;
	}

	// The input of the import form when it can be typed in.
	private static Regex EnabledImportField()
	{
		return new Regex($"<input\\b(?![^>]*\\bdisabled\\b)[^>]*id=\"{ImportField}\"", RegexOptions.None, TimeSpan.FromSeconds(5));
	}

	// The text of each tab, with the count chip appended to the one that has it, without the whitespace the markup is indented with.
	private static List<string> TabLabels(string html)
	{
		return [.. Regex.Matches(html, "<a [^>]*role=\"tab\"[^>]*>(.*?)</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(5))
			.Select(match => Regex.Replace(Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5)), "\\s+", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(5)))];
	}
}
