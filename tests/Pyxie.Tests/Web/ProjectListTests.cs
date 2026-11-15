using System.Text.RegularExpressions;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Repositories;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class ProjectListTests
{
	private const string FolderGone = "Folder is gone";
	private const string StopPipelineFirst = "Stop the pipeline first.";

	[Fact]
	public async Task Render_ListsEachProjectWithItsSourceAndAnOpenButton()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));

		Assert.Equal(["app", "web"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.Equal(["https://example.test/app.git", "https://example.test/web.git"], await list.Host.TextsWithClassAsync("project-source"));
		Assert.True((await list.Host.FindButtonAsync("Open app")).IsClickable, "A project whose folder is there can be opened.");
		Assert.True((await list.Host.FindButtonAsync("Open web")).IsClickable, "A project whose folder is there can be opened.");
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
		Assert.DoesNotContain("project-open-chip", await list.RenderHtmlAsync(), StringComparison.Ordinal);
		Assert.Empty(await list.Host.TextsWithClassAsync("import-bar"));
		Assert.DoesNotContain(FolderGone, await list.RenderHtmlAsync(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNothingWithoutProjects()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync();

		Assert.Equal(string.Empty, (await list.RenderHtmlAsync()).Trim());
	}

	[Fact]
	public async Task Render_SaysAProjectsFolderIsGoneAndOffersNoWayToOpenIt()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("moved", false));

		string html = await list.RenderHtmlAsync();
		int moved = html.IndexOf(">moved<", StringComparison.Ordinal);
		Assert.True(moved >= 0 && html.IndexOf(FolderGone, moved, StringComparison.Ordinal) > moved, $"The row of the project whose folder is gone should say so:{Environment.NewLine}{html}");
		Assert.Equal(1, html.Split(FolderGone).Length - 1);
		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		Assert.DoesNotContain(elements, element => element.Name == "button" && element.Attribute("aria-label") == "Open moved");
		Assert.True((await list.Host.FindButtonAsync("Open app")).IsClickable, "The other project can still be opened.");
	}

	[Fact]
	public async Task Render_MarksTheOpenProjectAndOffersToCloseItInsteadOfOpeningIt()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));

		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		GateRenderedElement openRow = Assert.Single(elements, element => ClassesOf(element).Contains("project-row-open"));
		Assert.Equal("li", openRow.Name);
		Assert.Matches(new Regex("<span class=\"pyx-chip project-open-chip\"[^>]*>Open</span>", RegexOptions.None, TimeSpan.FromSeconds(5)), await list.RenderHtmlAsync());
		Assert.True((await list.Host.FindButtonAsync("Close app")).IsClickable, "The open project can be closed.");
		Assert.DoesNotContain(elements, element => element.Name == "button" && element.Attribute("aria-label") == "Open app");
		Assert.Contains(elements, element => element.Name == "button" && element.Attribute("aria-label") == "Open web");
		Assert.DoesNotContain(elements, element => element.Name == "button" && element.Attribute("aria-label") == "Close web");
	}

	[Fact]
	public async Task Render_OffersOpenOnTheOtherProjectsWhileOneIsOpenWithoutAReason()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));

		GateRenderedElement web = await list.Host.FindButtonAsync("Open web");

		Assert.True(web.IsClickable, "Another project can be opened while one is open: opening it closes the open one.");
		Assert.False(web.Attributes.ContainsKey("disabled"), "The button is not disabled.");
		Assert.Empty(await TitlesOfActionsAsync(list));
	}

	[Fact]
	public async Task Render_DisablesOpenAndCloseWhileThePipelineRunsAndSaysWhyUntilItStops()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		Assert.Equal([string.Empty, string.Empty], await TitlesOfActionsAsync(list, includeUntitled: true));

		ResultAssert.Succeeded(list.Controller.Start());

		Assert.False((await list.Host.FindButtonAsync("Close app")).IsClickable, "The pipeline works in the open project, so it stays open.");
		Assert.False((await list.Host.FindButtonAsync("Open web")).IsClickable, "Nothing is opened while the pipeline runs.");
		Assert.Equal([StopPipelineFirst, StopPipelineFirst], await TitlesOfActionsAsync(list));

		list.Controller.Stop();

		Assert.True((await list.Host.FindButtonAsync("Close app")).IsClickable, "Close is offered again once the pipeline stopped.");
		Assert.True((await list.Host.FindButtonAsync("Open web")).IsClickable, "Open is offered again once the pipeline stopped.");
		Assert.Equal([string.Empty, string.Empty], await TitlesOfActionsAsync(list, includeUntitled: true));
	}

	[Fact]
	public async Task Open_OpensTheProjectAndAnnouncesIt()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open web"));

		Project? open = list.Rig.OpenProject.Current;
		Assert.Equal("web", open?.Name);
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
	}

	[Fact]
	public async Task Open_OnAnotherProjectWhileOneIsOpen_SwitchesToItAndAnnouncesItOnce()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open web"));

		Assert.Equal("web", list.Rig.OpenProject.Current?.Name);
		Assert.Equal(["web"], (await list.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken)).Where(project => project.IsOpen).Select(project => project.Name));
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
	}

	[Fact]
	public async Task Open_ShowsTheServicesRefusalOnTheProjectsRowAndLetsThePersonTryAgain()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		Project app = list.Projects[0];
		Folders.Delete(app.Folder);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open app"));

		Assert.Equal([$"The folder {app.Folder} is gone."], await list.Host.TextsWithClassAsync("project-error"));
		Assert.Null(list.Rig.OpenProject.Current);
		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		GateRenderedElement error = Assert.Single(elements, element => ClassesOf(element).Contains("project-error"));
		Assert.Equal("alert", error.Attribute("role"));
		GateRenderedElement webName = Assert.Single(elements, element => ClassesOf(element).Contains("project-name") && element.Text == "web");
		Assert.True(elements.ToList().IndexOf(error) < elements.ToList().IndexOf(webName), "The refusal belongs on the row of the project that was refused.");
		Assert.True((await list.Host.FindButtonAsync("Open web")).IsClickable, "Nothing is opening any more, so every project can be opened again.");
	}

	[Fact]
	public async Task Close_ClosesTheProjectAndAnnouncesIt()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Close app"));

		Assert.Null(list.Rig.OpenProject.Current);
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
	}

	[Fact]
	public async Task Close_ShowsTheServicesRefusalOnTheProjectsRowWhenTheListWasOutOfDate()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		GateRenderedElement close = await list.Host.FindButtonAsync("Close app");
		ResultAssert.Succeeded(await list.Rig.Catalog.CloseAsync(list.Projects[0].Id, TestContext.Current.CancellationToken));

		await list.Host.ClickAsync(close);

		Assert.Equal(["The project is not open."], await list.Host.TextsWithClassAsync("project-error"));
		Assert.True((await list.Host.FindButtonAsync("Close app")).IsClickable, "Nothing is closing any more, so the person can try again.");
	}

	[Fact]
	public async Task Close_OfAProjectThatASwitchJustClosed_IsRefusedOnItsRowAndLeavesTheNewlyOpenedProjectOpen()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open web"));

		// The list is not reloaded yet, as between the switch and the page reading the projects again, so it still offers to close app.
		GateRenderedElement staleClose = await list.Host.FindButtonAsync("Close app");
		Assert.True(staleClose.IsClickable, "The list still shows app as open, and nothing is changing, so Close is offered.");
		await list.Host.ClickAsync(staleClose);

		Assert.Equal("web", list.Rig.OpenProject.Current?.Name);
		Assert.Equal(["web"], (await list.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken)).Where(project => project.IsOpen).Select(project => project.Name));
		Assert.Equal(1, announcements);
		Assert.Equal(["The project is not open."], await list.Host.TextsWithClassAsync("project-error"));
		List<GateRenderedElement> elements = [.. await list.Host.ElementsAsync()];
		int errorAt = elements.IndexOf(Assert.Single(elements, element => ClassesOf(element).Contains("project-error")));
		int appAt = elements.IndexOf(Assert.Single(elements, element => ClassesOf(element).Contains("project-name") && element.Text == "app"));
		int webAt = elements.IndexOf(Assert.Single(elements, element => ClassesOf(element).Contains("project-name") && element.Text == "web"));
		Assert.True(appAt < errorAt && errorAt < webAt, "The refusal belongs on the row of app, the project that was refused.");

		await list.ReloadAsync();

		Assert.True((await list.Host.FindButtonAsync("Open app")).IsClickable, "The project the switch closed offers Open again once the list was read.");
		Assert.True((await list.Host.FindButtonAsync("Close web")).IsClickable, "The project the switch opened offers Close once the list was read.");
	}

	[Fact]
	public async Task Open_LeavesTheSameButtonOnTheProjectsRowAsCloseSoItKeepsTheFocus()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		GateRenderedElement open = await list.Host.FindButtonAsync("Open web");

		await list.Host.ClickAsync(open);
		await list.ReloadAsync();

		GateRenderedElement close = await list.Host.FindButtonAsync("Close web");
		Assert.Equal(open.Attribute("id"), close.Attribute("id"));
		Assert.Equal("Close", close.Text);
		Assert.True(close.IsClickable, "The project that was opened can be closed.");
		Assert.NotEqual(open.Attribute("id"), (await list.Host.FindButtonAsync("Open app")).Attribute("id"));
	}

	[Fact]
	public async Task Open_OnAnotherProjectWhileOneIsOpen_LeavesTheSameButtonOnTheProjectsRowAsCloseSoItKeepsTheFocus()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		GateRenderedElement open = await list.Host.FindButtonAsync("Open web");
		GateRenderedElement close = await list.Host.FindButtonAsync("Close app");

		await list.Host.ClickAsync(open);
		await list.ReloadAsync();

		GateRenderedElement switched = await list.Host.FindButtonAsync("Close web");
		Assert.Equal(open.Attribute("id"), switched.Attribute("id"));
		Assert.Equal("Close", switched.Text);
		Assert.True(switched.IsClickable, "The project that was opened can be closed.");
		GateRenderedElement reopenable = await list.Host.FindButtonAsync("Open app");
		Assert.Equal(close.Attribute("id"), reopenable.Attribute("id"));
		Assert.Equal("Open", reopenable.Text);
		Assert.True(reopenable.IsClickable, "The project that was closed by the switch can be opened again.");
	}

	[Fact]
	public async Task Close_LeavesTheSameButtonOnTheProjectsRowAsOpenSoItKeepsTheFocus()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		GateRenderedElement close = await list.Host.FindButtonAsync("Close app");

		await list.Host.ClickAsync(close);
		await list.ReloadAsync();

		GateRenderedElement open = await list.Host.FindButtonAsync("Open app");
		Assert.Equal(close.Attribute("id"), open.Attribute("id"));
		Assert.Equal("Open", open.Text);
		Assert.True(open.IsClickable, "The project that was closed can be opened again.");
	}

	[Fact]
	public async Task Reload_KeepsEachRowsButtonWhenTheListIsReadAgain()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		List<string?> before = [(await list.Host.FindButtonAsync("Close app")).Attribute("id"), (await list.Host.FindButtonAsync("Open web")).Attribute("id")];

		await list.ReloadAsync();

		Assert.Equal(before, [(await list.Host.FindButtonAsync("Close app")).Attribute("id"), (await list.Host.FindButtonAsync("Open web")).Attribute("id")]);
	}

	[Fact]
	public async Task Render_ShowsARunningImportAsAProjectWithAnIndeterminateBarAndNoCredentials()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true));

		list.StartHeldImport("https://someone:secret-token@example.com/owner/field-notes.git");

		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		Assert.Equal(["app", "field-notes"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.Matches(new Regex("<span class=\"project-action project-importing\"[^>]*>Importing</span>", RegexOptions.None, TimeSpan.FromSeconds(5)), await list.RenderHtmlAsync());
		Assert.Equal(["https://example.test/app.git", "https://example.com/owner/field-notes.git"], await list.Host.TextsWithClassAsync("project-source"));
		GateRenderedElement bar = Assert.Single(elements, element => element.Attribute("role") == "progressbar");
		Assert.Equal("Importing field-notes", bar.Attribute("aria-label"));
		Assert.False(bar.Attributes.ContainsKey("aria-valuenow"), "The import reports no progress, so the bar has no value: it is indeterminate.");
		Assert.DoesNotContain(elements, element => element.Text.Contains("secret-token", StringComparison.Ordinal) || element.Attributes.Values.Any(value => value.Contains("secret-token", StringComparison.Ordinal)));
		Assert.Equal(["Open app"], elements.Where(element => element.Name == "button").Select(element => element.Attribute("aria-label")));

		await list.FinishHeldImportAsync();

		Assert.Equal(["app"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.DoesNotContain(await list.Host.ElementsAsync(), element => element.Attribute("role") == "progressbar");
	}

	[Fact]
	public async Task Render_NamesARunningImportFromTheAddressWithoutItsCredentialsEvenWhenTheAddressHasNoPath()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync();

		list.StartHeldImport("https://someone:secret-token@example.com");

		Assert.Equal(["example.com"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.DoesNotContain(await list.Host.ElementsAsync(), element => element.Text.Contains("secret-token", StringComparison.Ordinal) || element.Attributes.Values.Any(value => value.Contains("secret-token", StringComparison.Ordinal)));
		await list.FinishHeldImportAsync();
	}

	[Fact]
	public async Task Dispose_StopsListeningToThePipelineAndTheImports()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true));
		Assert.Equal(1, EventSubscribers.Count(list.Controller, nameof(PipelineController.StateChanged)));
		Assert.Equal(1, EventSubscribers.Count(list.Imports.Imports, nameof(RepositoryImports.Changed)));

		await list.Host.CloseAsync();

		Assert.Equal(0, EventSubscribers.Count(list.Controller, nameof(PipelineController.StateChanged)));
		Assert.Equal(0, EventSubscribers.Count(list.Imports.Imports, nameof(RepositoryImports.Changed)));
	}

	private static string[] ClassesOf(GateRenderedElement element)
	{
		return (element.Attribute("class") ?? string.Empty).Split(' ');
	}

	// The reasons the wrappers of the Open and Close buttons give, in row order; the wrappers that give none only when asked, as empty text.
	private static async Task<string[]> TitlesOfActionsAsync(ProjectListHost list, bool includeUntitled = false)
	{
		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();

		return [.. elements.Where(element => ClassesOf(element).Contains("project-action") && element.Name == "span" && (includeUntitled || element.Attribute("title") is not null)).Select(element => element.Attribute("title") ?? string.Empty)];
	}
}
