using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Projects;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class OpenProjectViewTests
{
	private const string ClosedText = "Nothing is open";

	private readonly OpenProject _openProject = new();
	private readonly PipelineActivity _activity = new();
	private int _initialized;

	[Fact]
	public async Task Render_ShowsTheContentForTheOpenProject()
	{
		_openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness();

		string html = await (await RenderAsync(harness)).HtmlAsync();

		Assert.Contains("Project app", html, StringComparison.Ordinal);
		Assert.DoesNotContain(ClosedText, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsWhatItIsGivenForNoProjectWhileNoneIsOpen()
	{
		await using ComponentHarness harness = Harness();

		string html = await (await RenderAsync(harness)).HtmlAsync();

		Assert.Contains(ClosedText, html, StringComparison.Ordinal);
		Assert.DoesNotContain("Project", html, StringComparison.Ordinal);
		Assert.Equal(0, _initialized);
	}

	[Fact]
	public async Task Announce_ShowsTheContentOnceAProjectIsOpenedAndNothingOnceItIsClosed()
	{
		await using ComponentHarness harness = Harness();
		RenderedComponent<OpenProjectView> view = await RenderAsync(harness);

		_openProject.Set(Project(1, "app"));
		_activity.NotifyProjectChanged();
		await view.WaitForHtmlAsync(html => html.Contains("Project app", StringComparison.Ordinal), "The content should show once a project was opened");

		_openProject.Set(null);
		_activity.NotifyProjectChanged();
		await view.WaitForHtmlAsync(html => html.Contains(ClosedText, StringComparison.Ordinal) && !html.Contains("Project app", StringComparison.Ordinal), "The content should go once the project was closed");
	}

	[Fact]
	public async Task Announce_CreatesTheContentAgainForAnotherProjectEvenWhenItsCloseWasNeverShown()
	{
		_openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness();
		RenderedComponent<OpenProjectView> view = await RenderAsync(harness);
		Assert.Equal(1, _initialized);

		await view.RunAsync(_ =>
		{
			// The close and the open both land before the view refreshes, as they can when another tab switches projects while this one is busy.
			_openProject.Set(null);
			_openProject.Set(Project(2, "web"));
			_activity.NotifyProjectChanged();
		});
		await view.WaitForHtmlAsync(html => html.Contains("Project web", StringComparison.Ordinal), "The content should show the project opened next");

		Assert.Equal(2, _initialized);
	}

	[Fact]
	public async Task Announce_KeepsTheContentOfAProjectThatStaysOpen()
	{
		_openProject.Set(Project(1, "app"));
		await using ComponentHarness harness = Harness();
		RenderedComponent<OpenProjectView> view = await RenderAsync(harness);

		_activity.NotifyProjectChanged();
		await view.RunAsync(_ => { });

		Assert.Equal(1, _initialized);
	}

	[Fact]
	public async Task Render_ListensToProjectChangesOnlyWhileItLives()
	{
		await using ComponentHarness harness = Harness();
		RenderedComponent<OpenProjectView> view = await RenderAsync(harness);
		Assert.Equal(1, EventSubscribers.Count(_activity, nameof(PipelineActivity.ProjectChanged)));

		await view.RunAsync(instance => instance.Dispose());

		Assert.Equal(0, EventSubscribers.Count(_activity, nameof(PipelineActivity.ProjectChanged)));
	}

	private static Project Project(int id, string name)
	{
		return new Project { Id = id, Name = name, Folder = Path.Combine(Path.GetTempPath(), $"pyxie-{name}"), IsOpen = true };
	}

	private ComponentHarness Harness()
	{
		return new ComponentHarness(services =>
		{
			services.AddSingleton(_openProject);
			services.AddSingleton(_activity);
		});
	}

	// The content is a probe that counts the instances created of it and names the project it was given.
	private Task<RenderedComponent<OpenProjectView>> RenderAsync(ComponentHarness harness)
	{
		RenderFragment<Project> content = project => builder =>
		{
			builder.OpenComponent<InitializationProbe>(0);
			builder.AddComponentParameter(1, nameof(InitializationProbe.Initialized), (Action)(() => Interlocked.Increment(ref _initialized)));
			builder.AddComponentParameter(2, nameof(InitializationProbe.Text), $"Project {project.Name}");
			builder.CloseComponent();
		};
		RenderFragment closed = builder => builder.AddContent(0, ClosedText);

		return harness.RenderAsync<OpenProjectView>(new Dictionary<string, object>
		{
			[nameof(OpenProjectView.ChildContent)] = content,
			[nameof(OpenProjectView.Closed)] = closed,
		});
	}
}
