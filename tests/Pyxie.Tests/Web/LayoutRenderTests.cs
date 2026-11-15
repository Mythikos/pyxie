using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Layout;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class LayoutRenderTests
{
	[Fact]
	public async Task NavMenu_LinksBoardRepositoryAndSettingsInThatOrder()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<NavMenu> menu = await harness.RenderAsync<NavMenu>();

		string html = await menu.HtmlAsync();

		string[] links = [.. new[] { ">Board</a>", ">Repository</a>", ">Settings</a>" }];
		int[] positions = [.. links.Select(link => html.IndexOf(link, StringComparison.Ordinal))];
		Assert.DoesNotContain(-1, positions);
		Assert.Equal(positions.Order(), positions);
		Assert.Contains("href=\"repository\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Gates</a>", html, StringComparison.Ordinal);
		Assert.Contains("href=\"settings\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task NavMenu_LinksTheMarkAndWordmarkToTheBoard()
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<NavMenu> menu = await harness.RenderAsync<NavMenu>();

		string html = await menu.HtmlAsync();

		Match brand = Regex.Match(html, @"<a class=""app-brand""[^>]*>(?<content>.*?)</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(5));
		Assert.True(brand.Success, "The header should open with the brand link.");
		Assert.Matches(@"\bhref(?:="""")?[\s>]", brand.Value);
		Assert.Contains("aria-label=\"Pyxie home\"", brand.Value, StringComparison.Ordinal);
		Assert.Matches(@"<img class=""pyxie-mark""[^>]*src=""brand/pyxie-mark\.svg""", brand.Groups["content"].Value);
		Assert.Matches(@"<span class=""app-brand-name""[^>]*>pyxie</span>", brand.Groups["content"].Value);
	}

	[Fact]
	public async Task MainLayout_ShowsTheOpenProjectAndItsTargetBranchAfterTheLinksAndBeforeThePipeline()
	{
		OpenProject openProject = new();
		openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness(openProject, new PipelineActivity(), FixedRepositoryService.WithTarget("main"));
		RenderedComponent<MainLayout> layout = await RenderLayoutAsync(harness);

		string html = await layout.WaitForHtmlAsync(markup => markup.Contains("app-project-branch", StringComparison.Ordinal), "The header should show the target branch once the repository is read");

		int board = html.IndexOf(">Board</a>", StringComparison.Ordinal);
		int name = ProjectName().Match(html) is { Success: true } match && match.Groups["name"].Value == "app" ? match.Index : -1;
		int branch = html.IndexOf(">main</span>", StringComparison.Ordinal);
		int pipeline = html.IndexOf("pipeline-switch", StringComparison.Ordinal);
		Assert.True(board >= 0 && name > board && branch > name && pipeline > branch, $"The project's name and target branch belong after the links and before the pipeline. Positions: {board}, {name}, {branch}, {pipeline}");
	}

	[Fact]
	public async Task MainLayout_ShowsTheProjectWithoutABranchWhileItsRepositoryIsNotConnected()
	{
		OpenProject openProject = new();
		openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness(openProject, new PipelineActivity(), new FixedRepositoryService(null));
		RenderedComponent<MainLayout> layout = await RenderLayoutAsync(harness);

		string html = await layout.HtmlAsync();

		Assert.Matches(ProjectName(), html);
		Assert.DoesNotContain("app-project-branch", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task MainLayout_ShowsNoProjectWhileNoneIsOpen()
	{
		await using ComponentHarness harness = Harness(new OpenProject(), new PipelineActivity(), FixedRepositoryService.WithTarget("main"));
		RenderedComponent<MainLayout> layout = await RenderLayoutAsync(harness);

		string html = await layout.HtmlAsync();

		Assert.DoesNotMatch(ProjectName(), html);
		Assert.DoesNotContain("app-project-branch", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task MainLayout_FollowsTheOpenProjectWhenAChangeIsAnnounced()
	{
		OpenProject openProject = new();
		PipelineActivity activity = new();
		openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness(openProject, activity, FixedRepositoryService.WithTarget("main"));
		RenderedComponent<MainLayout> layout = await RenderLayoutAsync(harness);

		openProject.Set(null);
		activity.NotifyProjectChanged();
		await layout.WaitForHtmlAsync(html => !ProjectName().IsMatch(html), "The header should drop the name once the project was closed");

		openProject.Set(Project(2, "web"));
		activity.NotifyProjectChanged();
		await layout.WaitForHtmlAsync(html => ProjectName().Match(html).Groups["name"].Value == "web", "The header should show the name of the project opened next");
	}

	[Fact]
	public async Task MainLayout_ShowsThePipelineSwitchInTheHeaderAfterTheNavigation()
	{
		await using ComponentHarness harness = Harness(new OpenProject(), new PipelineActivity(), new FixedRepositoryService(null));
		RenderedComponent<MainLayout> layout = await RenderLayoutAsync(harness);

		string html = await layout.HtmlAsync();
		int navigation = html.IndexOf("class=\"app-nav\"", StringComparison.Ordinal);
		int pipeline = html.IndexOf(">Stopped<", StringComparison.Ordinal);
		int body = html.IndexOf("Page body", StringComparison.Ordinal);
		Assert.True(navigation >= 0 && pipeline > navigation && body > pipeline, $"The header should hold the navigation and then the pipeline switch, above the page. Positions: {navigation}, {pipeline}, {body}");
	}

	private static ComponentHarness Harness(OpenProject openProject, PipelineActivity activity, IRepositoryService repositories)
	{
		return new ComponentHarness(services =>
		{
			services.AddSingleton(openProject);
			services.AddSingleton(activity);
			services.AddSingleton(Options.Create(new PipelineOptions()));
			services.AddSingleton(new PipelineController(openProject));
			services.AddSingleton(repositories);
		});
	}

	private static Task<RenderedComponent<MainLayout>> RenderLayoutAsync(ComponentHarness harness)
	{
		return harness.RenderAsync<MainLayout>(new Dictionary<string, object> { [nameof(LayoutComponentBase.Body)] = (RenderFragment)(builder => builder.AddContent(0, "Page body")) });
	}

	private static Project Project(int id, string name)
	{
		return new Project { Id = id, Name = name, Folder = Path.Combine(Path.GetTempPath(), $"pyxie-{name}"), IsOpen = true };
	}

	// The element that shows the open project's name in the header, with the name it shows.
	private static Regex ProjectName()
	{
		return new Regex(@"<span[^>]*\bclass=""app-project""[^>]*>(?<name>[^<]*)</span>", RegexOptions.None, TimeSpan.FromSeconds(5));
	}
}
