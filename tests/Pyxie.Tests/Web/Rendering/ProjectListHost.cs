using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Projects;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The projects list open in an interactive host, over the real service that opens, closes, and removes projects (with the rig's stand-in sandboxes) and a catalog of projects the host stored without importing anything (see <see cref="StoredProjects"/>), so a test presses a project's Open, Close, or Remove the way a person would. The pipeline is the host's own controller, which the service that opens and closes projects asks, so a test starts it to see the list wait. Imports run in the background as in the application, against a catalog the test answers for, so a test holds one running to see its row.</summary>
internal sealed class ProjectListHost : IAsyncDisposable
{
	private readonly ServiceProvider _services;
	private readonly StubProjectCatalog _importCatalog;
	private readonly Dictionary<string, object?> _parameters;

	private ProjectListHost(RepositoryRig rig, IReadOnlyList<Project> projects, PipelineController controller, ImportsHost imports, StubProjectCatalog importCatalog, ServiceProvider services, GateInteractiveHost host, GateStubJsRuntime browser, Dictionary<string, object?> parameters)
	{
		Rig = rig;
		Browser = browser;
		Projects = projects;
		Controller = controller;
		Imports = imports;
		_importCatalog = importCatalog;
		_services = services;
		Host = host;
		_parameters = parameters;
	}

	/// <summary>Gets the services that hold the projects.</summary>
	public RepositoryRig Rig { get; }

	/// <summary>Gets the stored projects, in the order the list shows them; the open one, if the host opened one, is marked open.</summary>
	public IReadOnlyList<Project> Projects { get; }

	/// <summary>Gets the controller that starts and stops the pipeline, which the list follows and which refuses opening and closing while the pipeline runs.</summary>
	public PipelineController Controller { get; }

	/// <summary>Gets the background imports the list follows.</summary>
	public ImportsHost Imports { get; }

	/// <summary>Gets the host the list is open in.</summary>
	public GateInteractiveHost Host { get; }

	/// <summary>Gets the browser's script runtime, which records where the list moved the keyboard focus.</summary>
	public GateStubJsRuntime Browser { get; }

	/// <summary>Stores closed projects and opens the list of them, checking each project's folder as the Repository page does.</summary>
	/// <param name="projects">The projects to store, by name, and whether each has the folder opening it needs.</param>
	/// <returns>The open list.</returns>
	public static Task<ProjectListHost> OpenAsync(params (string Name, bool WithFolder)[] projects)
	{
		return CreateAsync(null, projects);
	}

	/// <summary>Stores projects, opens one of them, and opens the list of them.</summary>
	/// <param name="openName">The name of the project to open; it has its folder.</param>
	/// <param name="projects">The projects to store, by name, and whether each has the folder opening it needs.</param>
	/// <returns>The open list.</returns>
	public static Task<ProjectListHost> OpenWithOpenProjectAsync(string openName, params (string Name, bool WithFolder)[] projects)
	{
		return CreateAsync(openName, projects);
	}

	/// <summary>Reads the projects again and gives the list what it read, as the Repository page does when the announcement of an opened or closed project reaches it.</summary>
	/// <returns>A task that completes when the list has rendered.</returns>
	public async Task ReloadAsync()
	{
		IReadOnlyList<Project> projects = await Rig.Catalog.ListAsync(TestContext.Current.CancellationToken);
		_parameters[nameof(ProjectList.Projects)] = ToEntries(projects);

		await Host.UpdateAsync(_parameters);
	}

	/// <summary>Starts an import that waits at the catalog: a project is open as far as the import's check goes, so it keeps running until <see cref="FinishHeldImportAsync"/> and then fails with that reason, which needs no git.</summary>
	/// <param name="origin">The folder or URL to import, which may carry credentials.</param>
	public void StartHeldImport(string origin)
	{
		_importCatalog.Open = new Project { Id = 1, Name = "open", Folder = Path.Combine(Path.GetTempPath(), "pyxie-open-project"), IsOpen = true };
		_importCatalog.HoldAnswers = true;
		Assert.True(Imports.Imports.TryStart(RepositorySource.Url, origin), "No import should be running yet.");
	}

	/// <summary>Lets the held import continue, and waits until it has ended.</summary>
	/// <returns>A task that completes when the import has ended and reported how.</returns>
	public async Task FinishHeldImportAsync()
	{
		await _importCatalog.WaitForHeldAsync(1);
		_importCatalog.Answer(0);
		await Imports.Imports.Running;
	}

	/// <summary>Renders another copy of the list to HTML, for what the interactive host cannot read: Razor writes an element with no dynamic part, such as a project's "Folder is gone", as markup rather than as an element.</summary>
	/// <returns>The HTML of the list as it first renders.</returns>
	public async Task<string> RenderHtmlAsync()
	{
		await using HtmlRenderer renderer = new(_services, NullLoggerFactory.Instance);

		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			HtmlRootComponent root = await renderer.RenderComponentAsync<ProjectList>(ParameterView.FromDictionary(_parameters));

			return root.ToHtmlString();
		});
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Host.CloseAsync();
		await _services.DisposeAsync();
		await Imports.DisposeAsync();
		await Rig.DisposeAsync();
	}

	private static List<ProjectEntry> ToEntries(IEnumerable<Project> projects)
	{
		return [.. projects.Select(project => new ProjectEntry(project, ProjectSessions.IsFolderPresent(project)))];
	}

	private static async Task<ProjectListHost> CreateAsync(string? openName, (string Name, bool WithFolder)[] projects)
	{
		RepositoryRig rig = new();
		List<Project> stored = [];
		foreach ((string name, bool withFolder) in projects)
		{
			Project project = await StoredProjects.AddAsync(rig, name, withFolder);
			stored.Add(name == openName ? ResultAssert.Succeeded(await rig.Catalog.OpenAsync(project.Id, TestContext.Current.CancellationToken)) : project);
		}

		PipelineController controller = new(rig.OpenProject);
		ProjectSessions sessions = new(rig.Catalog, new SqliteProjectDatabase(), controller, rig.Activity, rig.Sandboxes, rig.ImageBuilds, rig.Layout, rig.Media);
		StubProjectCatalog importCatalog = new();
		ImportsHost imports = new(importCatalog);

		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		GateStubJsRuntime browser = new();
		services.AddSingleton<IJSRuntime>(browser);
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(sessions);
		services.AddSingleton(controller);
		services.AddSingleton(imports.Imports);
		ServiceProvider provider = services.BuildServiceProvider();
		GateInteractiveHost host = new(provider);
		Dictionary<string, object?> parameters = new() { [nameof(ProjectList.Projects)] = ToEntries(stored) };
		await host.StartAsync<ProjectList>(parameters);

		return new ProjectListHost(rig, stored, controller, imports, importCatalog, provider, host, browser, parameters);
	}
}
