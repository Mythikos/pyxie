using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Services;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Web.Components.Pages;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The Repository page rendered over the real project and repository services and real git repositories: projects live in a machine database and project folders under a workspace root of the rig's own, imports run in the background as in the application, and the announcements of project changes the page listens to are the services' own. The project has no cards, which the list of branches asks about. The pipeline is never started unless a test starts <see cref="Controller"/>, which the service that opens and closes projects asks and the projects panel follows.</summary>
internal sealed class RepositoryPage : IAsyncDisposable
{
	private readonly ComponentHarness _harness;

	private RepositoryPage(ComponentHarness harness, RenderedComponent<Repository> page, RepositoryRig rig, ImportsHost imports, PipelineController controller, ProjectSessions sessions)
	{
		_harness = harness;
		Page = page;
		Rig = rig;
		Imports = imports;
		Controller = controller;
		Sessions = sessions;
	}

	/// <summary>Gets the rendered page.</summary>
	public RenderedComponent<Repository> Page { get; }

	/// <summary>Gets the project and repository services the page reads, and the services that import projects.</summary>
	public RepositoryRig Rig { get; }

	/// <summary>Gets the background imports the page's import form follows.</summary>
	public ImportsHost Imports { get; }

	/// <summary>Gets the controller that starts and stops the pipeline, which the projects panel follows.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the service the page opens, closes, and removes projects with, over <see cref="Controller"/>.</summary>
	public ProjectSessions Sessions { get; }

	/// <summary>Renders the page.</summary>
	/// <param name="arrange">Prepares the rig before the page first reads it, for example by adding projects; nothing when it is not given.</param>
	/// <param name="tab">The tab the address names, as its query word; the Files tab when it is not given.</param>
	/// <param name="commit">The commit the address names, for the History tab; the list of commits when it is not given.</param>
	/// <returns>The rendered page. No project is open unless <paramref name="arrange"/> opened one.</returns>
	public static async Task<RepositoryPage> RenderAsync(Func<RepositoryRig, Task>? arrange = null, string? tab = null, string? commit = null)
	{
		RepositoryRig rig = new();
		if (arrange is not null)
		{
			await arrange(rig);
		}

		ImportsHost imports = new(rig);
		PipelineController controller = new(rig.OpenProject);
		ProjectSessions sessions = new(rig.Catalog, new SqliteProjectDatabase(), controller, rig.Activity, rig.Sandboxes, rig.ImageBuilds, rig.Layout, rig.Media);
		ComponentHarness harness = new(services =>
		{
			services.AddSingleton<ICardService, NoCardService>();
			services.AddSingleton<IRepositoryService>(rig.Repositories);
			services.AddSingleton<IProjectCatalog>(rig.Catalog);
			services.AddSingleton(rig.Browser);
			services.AddSingleton(rig.OpenProject);
			services.AddSingleton(sessions);
			services.AddSingleton(rig.Activity);
			services.AddSingleton(controller);
			services.AddSingleton(imports.Imports);
			services.AddSingleton(TimeProvider.System);
		});
		Dictionary<string, object> parameters = [];
		if (tab is not null)
		{
			parameters[nameof(Repository.TabQuery)] = tab;
		}

		if (commit is not null)
		{
			parameters[nameof(Repository.CommitQuery)] = commit;
		}

		return new RepositoryPage(harness, await harness.RenderAsync<Repository>(parameters), rig, imports, controller, sessions);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _harness.DisposeAsync();
		await Imports.DisposeAsync();
		await Rig.DisposeAsync();
	}
}
