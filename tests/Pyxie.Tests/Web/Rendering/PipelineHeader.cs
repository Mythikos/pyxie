using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Layout;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The pipeline switch rendered with the controller it controls, the announcements of project changes it listens to, and an open project the test opens and closes through a private machine database.</summary>
internal sealed class PipelineHeader : IAsyncDisposable
{
	private readonly MachineTestDatabase _machine;

	private PipelineHeader(MachineTestDatabase machine, ComponentHarness harness, PipelineController controller, PipelineActivity activity, RenderedComponent<PipelineSwitch> pipelineSwitch)
	{
		_machine = machine;
		Harness = harness;
		Controller = controller;
		Activity = activity;
		Switch = pipelineSwitch;
	}

	/// <summary>The number of workers the pipeline the switch shows is configured with.</summary>
	public const int Workers = 3;

	/// <summary>Gets the harness the switch was rendered by.</summary>
	public ComponentHarness Harness { get; }

	/// <summary>Gets the controller the switch starts and stops.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the announcements of project changes, which the switch listens to.</summary>
	public PipelineActivity Activity { get; }

	/// <summary>Gets the rendered switch.</summary>
	public RenderedComponent<PipelineSwitch> Switch { get; }

	/// <summary>Renders the switch.</summary>
	/// <param name="projectOpen">Whether a project is open when it is first rendered.</param>
	/// <returns>The rendered header.</returns>
	public static async Task<PipelineHeader> RenderAsync(bool projectOpen)
	{
		MachineTestDatabase machine = new();
		if (projectOpen)
		{
			await OpenAsync(machine);
		}

		PipelineController controller = new(machine.OpenProject);
		PipelineActivity activity = new();
		ComponentHarness harness = new(services =>
		{
			services.AddSingleton(machine.OpenProject);
			services.AddSingleton(controller);
			services.AddSingleton(activity);
			services.AddSingleton(Options.Create(new PipelineOptions { Workers = Workers }));
		});

		return new PipelineHeader(machine, harness, controller, activity, await harness.RenderAsync<PipelineSwitch>());
	}

	/// <summary>Opens a project, without announcing it.</summary>
	/// <returns>A task that completes when the project is open.</returns>
	public Task OpenProjectAsync()
	{
		return OpenAsync(_machine);
	}

	/// <summary>Closes the open project, without announcing it.</summary>
	/// <returns>A task that completes when no project is open.</returns>
	public async Task CloseProjectAsync()
	{
		ResultAssert.Succeeded(await _machine.Catalog.CloseAsync(TestContext.Current.CancellationToken));
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Harness.DisposeAsync();
		await _machine.DisposeAsync();
	}

	private static async Task OpenAsync(MachineTestDatabase machine)
	{
		Project project = ResultAssert.Succeeded(await machine.Catalog.AddAsync("app", Path.Combine(Path.GetTempPath(), "pyxie-header-project"), "https://example.test/app.git", TestContext.Current.CancellationToken));
		ResultAssert.Succeeded(await machine.Catalog.OpenAsync(project.Id, TestContext.Current.CancellationToken));
	}
}
