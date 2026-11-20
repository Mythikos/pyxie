using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Pyxie.Web.Components.Repository;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A repository copy button with a recording browser and a manually advanced clock.</summary>
internal sealed class RepositoryCopyButtonHost : IAsyncDisposable
{
	private readonly ComponentHarness _harness;

	private RepositoryCopyButtonHost(ComponentHarness harness, RenderedComponent<RepositoryCopyButton> button, RecordingJSRuntime browser, ManualTimeProvider clock)
	{
		_harness = harness;
		Button = button;
		Browser = browser;
		Clock = clock;
	}

	/// <summary>Gets the rendered button.</summary>
	public RenderedComponent<RepositoryCopyButton> Button { get; }

	/// <summary>Gets the browser the button calls, which records the calls.</summary>
	public RecordingJSRuntime Browser { get; }

	/// <summary>Gets the clock the button's timers run on.</summary>
	public ManualTimeProvider Clock { get; }

	/// <summary>Renders a button that copies a repository location.</summary>
	/// <param name="text">The location to copy.</param>
	/// <returns>The rendered button.</returns>
	public static async Task<RepositoryCopyButtonHost> RenderAsync(string text)
	{
		RecordingJSRuntime browser = new();
		ManualTimeProvider clock = new();
		ComponentHarness harness = new(services =>
		{
			services.AddSingleton<IJSRuntime>(browser);
			services.AddSingleton<TimeProvider>(clock);
		});

		RenderedComponent<RepositoryCopyButton> button = await harness.RenderAsync<RepositoryCopyButton>(new Dictionary<string, object>
		{
			[nameof(RepositoryCopyButton.Label)] = "Copy hub path",
			[nameof(RepositoryCopyButton.Text)] = text,
		});

		return new RepositoryCopyButtonHost(harness, button, browser, clock);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _harness.DisposeAsync();
	}
}
