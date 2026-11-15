using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Web.Components.Repository;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The import form open in an interactive host, over the application's background imports. An import the test starts really runs, against a project catalog the test answers for: with the imports held, a project is already open, so an import waits for <see cref="FinishHeldImportAsync"/> and then fails with that reason, which needs no git. Over real imports, an import of a git repository the test made succeeds.</summary>
internal sealed class ConnectCard : IAsyncDisposable
{
	private readonly ServiceProvider _services;

	private ConnectCard(ImportsHost imports, StubProjectCatalog catalog, ServiceProvider services, GateInteractiveHost host)
	{
		Imports = imports;
		Catalog = catalog;
		_services = services;
		Host = host;
	}

	/// <summary>Gets the background imports and what runs them.</summary>
	public ImportsHost Imports { get; }

	/// <summary>Gets the project catalog the imports ask whether a project is open; with real imports it is not the one they use.</summary>
	public StubProjectCatalog Catalog { get; }

	/// <summary>Gets the host the form is open in.</summary>
	public GateInteractiveHost Host { get; }

	/// <summary>Gets the full path of a folder that does not exist, which an import refuses at once.</summary>
	public string MissingFolder { get; } = Path.Combine(Path.GetTempPath(), $"pyxie-missing-{Guid.NewGuid():N}");

	/// <summary>Opens the form.</summary>
	/// <param name="holdImports">Whether imports wait, once they have checked their source, until <see cref="FinishHeldImportAsync"/>.</param>
	/// <param name="projectOpen">Whether the page tells the form a project is open.</param>
	/// <returns>The open form.</returns>
	public static async Task<ConnectCard> OpenAsync(bool holdImports = false, bool projectOpen = false)
	{
		StubProjectCatalog catalog = new();

		if (holdImports)
		{
			catalog.Open = new Project { Id = 1, Name = "open", Folder = Path.Combine(Path.GetTempPath(), "pyxie-open-project"), IsOpen = true };
			catalog.HoldAnswers = true;
		}

		return await OpenOverAsync(new ImportsHost(catalog), catalog, projectOpen);
	}

	/// <summary>Opens the form over imports that really import: a git repository the test made is cloned into a workspace of the host's own.</summary>
	/// <returns>The open form. Its <see cref="Catalog"/> answers nothing.</returns>
	public static async Task<ConnectCard> OpenOverRealImportsAsync()
	{
		return await OpenOverAsync(new ImportsHost(), new StubProjectCatalog(), projectOpen: false);
	}

	private static async Task<ConnectCard> OpenOverAsync(ImportsHost imports, StubProjectCatalog catalog, bool projectOpen)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton<IJSRuntime, GateStubJsRuntime>();
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(imports.Imports);
		ServiceProvider provider = services.BuildServiceProvider();
		GateInteractiveHost host = new(provider);
		await host.StartAsync<RepositoryConnect>(new Dictionary<string, object?> { [nameof(RepositoryConnect.ProjectOpen)] = projectOpen });

		return new ConnectCard(imports, catalog, provider, host);
	}

	/// <summary>Opens the form again, as a second tab, or a later visit to the page, would.</summary>
	/// <returns>A host the form is not yet open in; the caller starts the form in it and disposes it.</returns>
	public GateInteractiveHost OpenAnother()
	{
		return new GateInteractiveHost(_services);
	}

	/// <summary>Lets the import that waits for its answer continue, and waits until it has ended.</summary>
	/// <returns>A task that completes when the import has ended and reported how.</returns>
	public async Task FinishHeldImportAsync()
	{
		await Catalog.WaitForHeldAsync(1);
		Catalog.Answer(0);
		await Imports.Imports.Running;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Host.CloseAsync();
		await _services.DisposeAsync();
		await Imports.DisposeAsync();
	}
}
