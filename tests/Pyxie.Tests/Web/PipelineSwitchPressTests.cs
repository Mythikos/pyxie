using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

using Pyxie.Data.Projects;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Layout;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The header's one button, pressed the way a person presses it: what a press does follows the label the button showed when it was pressed, and the button a person pressed is still there to press again. A press the browser sent before it showed the change, and the second click of a double click, do not undo it.</summary>
public sealed class PipelineSwitchPressTests
{
	[Fact]
	public async Task Press_StartsTheStoppedPipelineAndThenStopsTheRunningOneWithTheSameButton()
	{
		OpenProject openProject = StandInProject.Open();
		PipelineController controller = new(openProject);
		await using ServiceProvider services = CreateServices(openProject, controller);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<PipelineSwitch>();
		GateRenderedElement start = await host.FindButtonAsync("Start");

		await host.ClickAsync(start);

		Assert.True(controller.IsRunning, "Pressing Start should start the pipeline.");
		GateRenderedElement stop = await host.FindButtonAsync("Stop");
		Assert.Equal(start.Attribute("id"), stop.Attribute("id"));

		await host.ClickAsync(stop);

		Assert.False(controller.IsRunning, "Pressing Stop should stop the pipeline.");
		GateRenderedElement startAgain = await host.FindButtonAsync("Start");
		Assert.Equal(start.Attribute("id"), startAgain.Attribute("id"));
	}

	[Fact]
	public async Task Start_PressedAgainBeforeTheBrowserShowedStop_LeavesThePipelineRunning()
	{
		OpenProject openProject = StandInProject.Open();
		PipelineController controller = new(openProject);
		await using ServiceProvider services = CreateServices(openProject, controller);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<PipelineSwitch>();
		GateRenderedElement start = await host.FindButtonAsync("Start");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(start);
		await host.ClickAsync(start);
		await host.AcknowledgeAsync();

		Assert.True(controller.IsRunning, "A press the browser sent while it still showed Start should not stop the pipeline the first press started.");
	}

	[Fact]
	public async Task Stop_PressedAgainBeforeTheBrowserShowedStart_LeavesThePipelineStopped()
	{
		OpenProject openProject = StandInProject.Open();
		PipelineController controller = new(openProject);
		ResultAssert.Succeeded(controller.Start());
		await using ServiceProvider services = CreateServices(openProject, controller);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<PipelineSwitch>();
		GateRenderedElement stop = await host.FindButtonAsync("Stop");
		await host.HoldAcknowledgementsAsync();

		await host.ClickAsync(stop);
		await host.ClickAsync(stop);
		await host.AcknowledgeAsync();

		Assert.False(controller.IsRunning, "A press the browser sent while it still showed Stop should not start the pipeline the first press stopped.");
	}

	[Fact]
	public async Task Start_DoubleClicked_LeavesThePipelineRunning()
	{
		OpenProject openProject = StandInProject.Open();
		PipelineController controller = new(openProject);
		await using ServiceProvider services = CreateServices(openProject, controller);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<PipelineSwitch>();
		await host.ClickAsync(await host.FindButtonAsync("Start"));

		await host.ClickAsync(await host.FindButtonAsync("Stop"), clicks: 2);

		Assert.True(controller.IsRunning, "The second click of a double click on Start should not stop the pipeline, though the browser already shows Stop.");
	}

	[Fact]
	public async Task Stop_DoubleClicked_LeavesThePipelineStopped()
	{
		OpenProject openProject = StandInProject.Open();
		PipelineController controller = new(openProject);
		ResultAssert.Succeeded(controller.Start());
		await using ServiceProvider services = CreateServices(openProject, controller);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<PipelineSwitch>();
		await host.ClickAsync(await host.FindButtonAsync("Stop"));

		await host.ClickAsync(await host.FindButtonAsync("Start"), clicks: 2);

		Assert.False(controller.IsRunning, "The second click of a double click on Stop should not start the pipeline, though the browser already shows Start.");
	}

	private static ServiceProvider CreateServices(OpenProject openProject, PipelineController controller)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton<IJSRuntime, GateStubJsRuntime>();
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(openProject);
		services.AddSingleton(controller);
		services.AddSingleton(new PipelineActivity());
		services.AddSingleton(Options.Create(new PipelineOptions { Workers = 3 }));

		return services.BuildServiceProvider();
	}
}
