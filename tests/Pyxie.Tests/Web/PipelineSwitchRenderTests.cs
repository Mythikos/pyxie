using System.Text.RegularExpressions;

using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class PipelineSwitchRenderTests
{
	private const string ProjectRequired = "No project is open.";
	private const string StopReason = "Docker is not running. Start Docker Desktop, then start the pipeline again.";

	[Fact]
	public async Task Render_DisablesStartAndSaysWhyWhileNoProjectIsOpen()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: false);

		string html = await header.Switch.HtmlAsync();

		Assert.Contains(">Stopped<", html, StringComparison.Ordinal);
		Assert.Matches(@"<button[^>]*\bdisabled\b[^>]*>(?:(?!</button>).)*Start</button>", html);
		Assert.Contains($"title=\"{ProjectRequired}\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_EnablesStartWhenAProjectIsOpen()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);

		string html = await header.Switch.HtmlAsync();

		Assert.Matches(@"<button(?![^>]*\bdisabled\b)[^>]*>(?:(?!</button>).)*Start</button>", html);
		Assert.DoesNotContain("title=", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsStopInsteadOfStartWhileThePipelineRuns()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();

		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs");

		Assert.Contains(">Stop</button>", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Start</button>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheRunningRoundsOutOfTheWorkersWhileThePipelineRuns()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains($">0 of {PipelineHeader.Workers}<", StringComparison.Ordinal), "The header should show that no worker has a card yet");

		header.Controller.RoundStarted();
		header.Controller.RoundStarted();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains($">2 of {PipelineHeader.Workers}<", StringComparison.Ordinal), "The header should count the rounds that started");

		header.Controller.RoundEnded();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains($">1 of {PipelineHeader.Workers}<", StringComparison.Ordinal), "The header should count down when a round ends");
	}

	[Fact]
	public async Task Render_ShowsNoWorkerCountWhileStopped()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);

		string html = await header.Switch.HtmlAsync();

		Assert.DoesNotContain("pipeline-capacity", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheMarkAwakeWhileRunningAndAsleepWhileStopped()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		Assert.Contains("brand/pyxie-mark-asleep.svg", await header.Switch.HtmlAsync(), StringComparison.Ordinal);

		header.Controller.Start();
		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs");

		Assert.Contains("brand/pyxie-mark.svg", html, StringComparison.Ordinal);
		Assert.DoesNotContain("pyxie-mark-asleep", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_KeepsTheSameButtonWhenThePipelineStartsAndStops()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		string whileStopped = ButtonId(await header.Switch.HtmlAsync());

		header.Controller.Start();
		string whileRunning = ButtonId(await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs"));
		header.Controller.Stop();
		string stoppedAgain = ButtonId(await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Stopped<", StringComparison.Ordinal), "The header should show that the pipeline stopped"));

		Assert.Equal(whileStopped, whileRunning);
		Assert.Equal(whileStopped, stoppedAgain);
	}

	[Fact]
	public async Task Render_GivesNoTitleWhileRunning()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();

		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs");

		Assert.DoesNotContain("title=", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_FollowsTheControllerBackToStopped()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs");

		header.Controller.Stop();
		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Stopped<", StringComparison.Ordinal), "The header should show that the pipeline stopped");

		Assert.Contains(">Start</button>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsWhyThePipelineStoppedByItselfWhileItStaysStopped()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs");

		header.Controller.StopBecause(StopReason);
		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Stopped<", StringComparison.Ordinal), "The header should show that the pipeline stopped");

		Assert.Matches($"<span class=\"pipeline-stop-reason\" role=\"status\"[^>]*>{Regex.Escape(StopReason)}</span>", html);
		Assert.Contains(">Start</button>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_HidesTheStopReasonOnceThePipelineRunsAgain()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();
		header.Controller.StopBecause(StopReason);
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains(StopReason, StringComparison.Ordinal), "The header should say why the pipeline stopped");

		header.Controller.Start();
		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs again");

		Assert.DoesNotContain(StopReason, html, StringComparison.Ordinal);
		Assert.DoesNotContain("pipeline-stop-reason", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNoStopReasonOnceAPersonStopsThePipelineAfterARestart()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();
		header.Controller.StopBecause(StopReason);
		header.Controller.Start();
		await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Running<", StringComparison.Ordinal), "The header should show that the pipeline runs again");

		header.Controller.Stop();
		string html = await header.Switch.WaitForHtmlAsync(markup => markup.Contains(">Stopped<", StringComparison.Ordinal), "The header should show that the pipeline stopped");

		Assert.DoesNotContain(StopReason, html, StringComparison.Ordinal);
		Assert.DoesNotContain("pipeline-stop-reason", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNoStopReasonBeforeThePipelineEverRan()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);

		string html = await header.Switch.HtmlAsync();

		Assert.Contains(">Stopped<", html, StringComparison.Ordinal);
		Assert.DoesNotContain("pipeline-stop-reason", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_EnablesStartAsSoonAsAProjectIsOpenedAndAnnounced()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: false);
		await header.OpenProjectAsync();

		header.Activity.NotifyProjectChanged();

		await header.Switch.WaitForHtmlAsync(markup => !markup.Contains(ProjectRequired, StringComparison.Ordinal), "The header should enable Start once a project is open");
	}

	[Fact]
	public async Task Render_DisablesStartAndSaysWhyAsSoonAsTheProjectIsClosedAndAnnounced()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		await header.CloseProjectAsync();

		header.Activity.NotifyProjectChanged();

		await header.Switch.WaitForHtmlAsync(markup => markup.Contains(ProjectRequired, StringComparison.Ordinal), "The header should disable Start once no project is open");
	}

	[Fact]
	public async Task Render_ListensToTheControllerOnlyWhileItLives()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		Assert.Equal(1, EventSubscribers.Count(header.Controller, nameof(PipelineController.StateChanged)));

		await header.Switch.RunAsync(instance => instance.Dispose());

		Assert.Equal(0, EventSubscribers.Count(header.Controller, nameof(PipelineController.StateChanged)));
	}

	[Fact]
	public async Task Render_ListensToProjectChangesOnlyWhileItLives()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		Assert.Equal(1, EventSubscribers.Count(header.Activity, nameof(PipelineActivity.ProjectChanged)));

		await header.Switch.RunAsync(instance => instance.Dispose());

		Assert.Equal(0, EventSubscribers.Count(header.Activity, nameof(PipelineActivity.ProjectChanged)));
	}

	[Fact]
	public async Task Start_StartsThePipelineWhenAProjectIsOpen()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);

		await header.Switch.ClickAsync("StartPipeline");

		Assert.True(header.Controller.IsRunning, "Start should start the pipeline.");
	}

	[Fact]
	public async Task Start_LeavesThePipelineStoppedWhenTheProjectClosedSinceTheHeaderLastLooked()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		await header.CloseProjectAsync();

		await header.Switch.ClickAsync("StartPipeline");

		Assert.False(header.Controller.IsRunning, "No project is open, so the controller refuses to start.");
	}

	[Fact]
	public async Task Stop_StopsARunningPipeline()
	{
		await using PipelineHeader header = await PipelineHeader.RenderAsync(projectOpen: true);
		header.Controller.Start();

		await header.Switch.ClickAsync("StopPipeline");

		Assert.False(header.Controller.IsRunning, "Stop should stop the pipeline.");
	}

	// The identifier Blazorise gives a button when it creates it: the same value means the same button, and so the same element in the browser, which keeps the keyboard focus.
	private static string ButtonId(string html)
	{
		Match button = Regex.Match(html, @"<button\b[^>]*\bid=""([^""]+)""");
		Assert.True(button.Success, $"The switch should render a button:{Environment.NewLine}{html}");

		return button.Groups[1].Value;
	}
}
