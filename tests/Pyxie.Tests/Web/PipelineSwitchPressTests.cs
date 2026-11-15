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

/// <summary>The header's one button, pressed the way a person presses it: what a press does follows the label the button shows, and the button a person pressed is still there to press again.</summary>
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
