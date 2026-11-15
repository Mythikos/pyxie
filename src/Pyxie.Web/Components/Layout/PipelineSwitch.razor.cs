using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

using Pyxie.Data.Projects;
using Pyxie.Runtime.Orchestration;
using Pyxie.Web.Components.Projects;

namespace Pyxie.Web.Components.Layout;

/// <summary>The header control that shows whether the pipeline is running, how many of its workers have a card, and starts or stops it with one button that is Start while the pipeline is stopped and Stop while it runs. It is the same button either way, so pressing it keeps the keyboard focus. Starting needs an open project, so it follows projects being imported, opened, or closed, on any page and in any tab.</summary>
public partial class PipelineSwitch : IDisposable
{
	private bool _disposed;

	/// <summary>Gets or sets the pipeline controller, supplied by the framework.</summary>
	[Inject]
	public required PipelineController Controller { get; set; }

	/// <summary>Gets or sets the open project, supplied by the framework.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <summary>Gets or sets the announcements of changes to the open project, supplied by the framework.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

	/// <summary>Gets or sets how the pipeline runs, supplied by the framework; the header shows its number of workers.</summary>
	[Inject]
	public required IOptions<PipelineOptions> Options { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Controller.StateChanged -= OnChanged;
		Activity.ProjectChanged -= OnChanged;
	}

	/// <inheritdoc/>
	protected override void OnInitialized()
	{
		Controller.StateChanged += OnChanged;
		Activity.ProjectChanged += OnChanged;
	}

	private bool IsRunning => Controller.IsRunning;

	private bool ProjectOpen => OpenProject.Current is not null;

	private string StateText => IsRunning ? "Running" : "Stopped";

	// The cards the pipeline is working on now, out of the cards it can work on at once.
	private string CapacityText => string.Create(CultureInfo.InvariantCulture, $"{Controller.RunningRounds} of {Options.Value.Workers}");

	// Why the pipeline stopped by itself, such as Docker not running, until it is started again.
	private string? StopReason => IsRunning ? null : Controller.StopReason;

	private string PillClass => IsRunning ? "pipeline-pill pipeline-pill-running" : "pipeline-pill";

	private string ActionText => IsRunning ? "Stop" : "Start";

	private string ActionClass => IsRunning ? "pipeline-stop" : "pipeline-start";

	// What a press does is what the button says when it is drawn, so a press never does the opposite of its label if the pipeline changed state just before it.
	private EventCallback ActionClicked => IsRunning ? EventCallback.Factory.Create(this, StopPipeline) : EventCallback.Factory.Create(this, StartPipelineAsync);

	// The reason the disabled Start button gives when hovered; the browser shows it for the wrapper, which still receives the pointer when the button does not. A running pipeline has a project open and gives none.
	private string? StartBlockedText => IsRunning || ProjectOpen ? null : NoProjectOpen.Text;

	// Raised on the thread that started or stopped the pipeline or changed the project, which can be a background import: the render moves onto the component's own context.
	private void OnChanged()
	{
		_ = InvokeAsync(Refresh);
	}

	private void Refresh()
	{
		if (!_disposed)
		{
			StateHasChanged();
		}
	}

	// The controller refuses to start without an open project; the button is already disabled then, so a refusal only means the project closed a moment ago and the next render shows it.
	private Task StartPipelineAsync()
	{
		Controller.Start();

		return Task.CompletedTask;
	}

	private void StopPipeline()
	{
		Controller.Stop();
	}
}
