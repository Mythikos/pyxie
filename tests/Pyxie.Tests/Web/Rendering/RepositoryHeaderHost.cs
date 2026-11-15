using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Pyxie.Web.Components.Repository;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The repository header of an open project, rendered with a browser that records what it is asked to do and a clock that only moves when the test says. Its hub is a folder name the test chose, and nothing is behind it. Disposing it disposes the header, as leaving the page does.</summary>
internal sealed class RepositoryHeaderHost : IAsyncDisposable
{
	private readonly ComponentHarness _harness;

	private RepositoryHeaderHost(ComponentHarness harness, RenderedComponent<RepositoryHeader> header, RecordingJSRuntime browser, ManualTimeProvider clock)
	{
		_harness = harness;
		Header = header;
		Browser = browser;
		Clock = clock;
	}

	/// <summary>Gets the rendered header.</summary>
	public RenderedComponent<RepositoryHeader> Header { get; }

	/// <summary>Gets the browser the header calls, which records the calls.</summary>
	public RecordingJSRuntime Browser { get; }

	/// <summary>Gets the clock the header's timers run on.</summary>
	public ManualTimeProvider Clock { get; }

	/// <summary>Renders the header of a repository, in an open project.</summary>
	/// <param name="hubPath">The folder of the hub, which the header shows and copies.</param>
	/// <param name="origin">The folder or URL the repository was imported from.</param>
	/// <param name="projectName">The open project's name.</param>
	/// <param name="targetBranch">The branch finished cards are merged into.</param>
	/// <returns>The rendered header.</returns>
	public static async Task<RepositoryHeaderHost> RenderAsync(string hubPath = "clone", string origin = "origin", string projectName = "app", string targetBranch = "main")
	{
		RecordingJSRuntime browser = new();
		ManualTimeProvider clock = new();
		ComponentHarness harness = new(services =>
		{
			services.AddSingleton<IJSRuntime>(browser);
			services.AddSingleton<TimeProvider>(clock);
		});
		RepositoryEntity repository = new() { Path = hubPath, Origin = origin, TargetBranch = targetBranch };

		RenderedComponent<RepositoryHeader> header = await harness.RenderAsync<RepositoryHeader>(new Dictionary<string, object>
		{
			[nameof(RepositoryHeader.ProjectName)] = projectName,
			[nameof(RepositoryHeader.Repository)] = repository,
		});

		return new RepositoryHeaderHost(harness, header, browser, clock);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _harness.DisposeAsync();
	}
}
