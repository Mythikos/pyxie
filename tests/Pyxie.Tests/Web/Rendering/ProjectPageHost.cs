using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Renders the pages that belong to a project (Settings) over the real gate, agent, and category services on a test database, with an open project the test opens and closes and the announcements of project changes the pages follow. The test database stands in for whichever project is open.</summary>
internal sealed class ProjectPageHost : IAsyncDisposable
{
	private readonly string _workspace = TempFolder.Create("pyx-mcp-settings");
	private readonly ComponentHarness _harness;
	private readonly OpenProject _openProject = new();
	private readonly PipelineActivity _activity = new();

	/// <summary>Initializes a new instance of the <see cref="ProjectPageHost"/> class, with no project open.</summary>
	/// <param name="database">The database the pages' services read and write.</param>
	/// <param name="jsRuntime">The script runtime that lets a page be rendered interactively, which a render to HTML has no use for, or <see langword="null"/> to leave scripts unavailable as they are in a render to HTML.</param>
	public ProjectPageHost(TestDatabase database, IJSRuntime? jsRuntime = null)
	{
		_harness = new ComponentHarness(services =>
		{
			if (jsRuntime is not null)
			{
				services.AddSingleton(jsRuntime);
			}

			services.AddSingleton(new McpAccessSettings(new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = _workspace }))));
			services.AddSingleton(_openProject);
			services.AddSingleton(_activity);
			services.AddSingleton<IGateService>(new GateService(database.ContextFactory));
			services.AddSingleton<IAgentService>(new AgentService(database.ContextFactory, database.Clock));
			services.AddSingleton<ICategoryService>(new CategoryService(database.ContextFactory));
			services.AddSingleton<ISandboxSettingsService>(new SandboxSettingsService(database.ContextFactory, database.Clock));
			services.AddSingleton<ISettingsTransferService>(new SettingsTransferService(database.ContextSource, _openProject, database.Clock));
			services.AddSingleton<ISandboxes, LocalSandboxes>(_ => new LocalSandboxes(new ProcessRunner(TimeProvider.System), Path.Combine(Path.GetTempPath(), $"pyxie-page-sandboxes-{Guid.NewGuid():N}")));
			services.AddSingleton(provider => new SandboxImageBuilds(provider.GetRequiredService<ISandboxes>(), database.Clock));
			services.AddSingleton<ISandboxSignIns, StandInSignIns>();
		});
	}

	/// <summary>Gets the announcements of project changes, which the pages follow.</summary>
	public PipelineActivity Activity => _activity;

	/// <summary>Gets the services the pages are given, for rendering a page interactively with a <see cref="GateInteractiveHost"/>.</summary>
	public IServiceProvider Services => _harness.Services;

	/// <summary>Gets the navigation manager the pages are given, which a test moves as following a link does.</summary>
	public TestNavigationManager Navigation => _harness.Navigation;

	/// <summary>Renders a page.</summary>
	/// <typeparam name="TPage">The page.</typeparam>
	/// <returns>The rendered page.</returns>
	public Task<RenderedComponent<TPage>> RenderAsync<TPage>()
		where TPage : IComponent
	{
		return _harness.RenderAsync<TPage>();
	}

	/// <summary>Opens a project and announces it, as opening one on the Repository page does.</summary>
	public void OpenAndAnnounce()
	{
		_openProject.Set(new Project { Id = 1, Name = "app", Folder = Path.Combine(Path.GetTempPath(), "pyxie-app"), IsOpen = true });
		_activity.NotifyProjectChanged();
	}

	/// <summary>Closes the open project and announces it, as closing it on the Repository page does.</summary>
	public void CloseAndAnnounce()
	{
		_openProject.Set(null);
		_activity.NotifyProjectChanged();
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _harness.DisposeAsync();
		await TempFolder.DeleteAsync(_workspace);
	}
}
