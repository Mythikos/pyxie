using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Transfer;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Builds the services the Export and Import controls, the import dialog, and the Settings page that holds them run with: the real settings transfer service on a test database for a stand-in open project, and the real agent, gate, category, and sandbox settings services the page's panels read, with scripted sandboxes and no sign-ins. It also previews a settings file the way Import does, so a dialog opens on what a person would review.</summary>
internal static class SettingsTransferHost
{
	/// <summary>Gets the stand-in project that is open, whose settings the components export and import into.</summary>
	public static Project Project => StandInProject.Open().Current ?? throw new InvalidOperationException("The stand-in project should be open.");

	/// <summary>Creates the services of the components.</summary>
	/// <param name="database">The database the services read and write.</param>
	/// <param name="transfer">The settings transfer service to use instead of the real one, such as one whose imports a test holds, or <see langword="null"/> for the real one.</param>
	/// <param name="jsRuntime">The script runtime to use instead of the one that does nothing, such as one of a browser that refuses to open a file picker, or <see langword="null"/> for the one that does nothing.</param>
	/// <returns>The service provider; the caller disposes it.</returns>
	public static ServiceProvider CreateServices(TestDatabase database, ISettingsTransferService? transfer = null, IJSRuntime? jsRuntime = null)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton(jsRuntime ?? new GateStubJsRuntime());
		services.AddSingleton<NavigationManager>(new TestNavigationManager());
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		OpenProject openProject = StandInProject.Open();
		services.AddSingleton(openProject);
		services.AddSingleton<PipelineController>();
		services.AddSingleton<ScratchFolder>();
		services.AddSingleton(provider => new McpAccessSettings(new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = provider.GetRequiredService<ScratchFolder>().Folder }))));
		services.AddSingleton(new PipelineActivity());
		services.AddSingleton(transfer ?? NewService(database, openProject));
		services.AddSingleton<IAgentService>(new AgentService(database.ContextFactory, database.Clock));
		services.AddSingleton<IGateService>(new GateService(database.ContextFactory));
		services.AddSingleton<ICategoryService>(new CategoryService(database.ContextFactory));
		services.AddSingleton<ISandboxSettingsService>(new SandboxSettingsService(database.ContextFactory, database.Clock));
		services.AddSingleton<ISandboxes>(new ScriptedSandboxes());
		services.AddSingleton(provider => new SandboxImageBuilds(provider.GetRequiredService<ISandboxes>(), database.Clock));
		services.AddSingleton<ISandboxSignIns>(new FixedSignIns(SandboxEditorHost.NoSignIns));

		return services.BuildServiceProvider();
	}

	/// <summary>Creates the real settings transfer service on a test database, for the stand-in project.</summary>
	/// <param name="database">The database the service reads and writes.</param>
	/// <param name="openProject">The open project the service checks each operation against, which a test can close; a holder of its own with the stand-in project open unless given.</param>
	/// <returns>The service.</returns>
	public static ISettingsTransferService NewService(TestDatabase database, OpenProject? openProject = null)
	{
		return new SettingsTransferService(database.ContextSource, openProject ?? StandInProject.Open(), database.Clock);
	}

	/// <summary>Reads what importing a settings file into the stand-in project would do, as Import does once the file is read, and returns what the import dialog opens with.</summary>
	/// <param name="database">The database the project's settings are read from.</param>
	/// <param name="settings">The file's settings.</param>
	/// <returns>The file's settings with their preview.</returns>
	public static async Task<SettingsImportOpening> OpenAsync(TestDatabase database, ProjectSettings settings)
	{
		SettingsImportPreview preview = ResultAssert.Succeeded(await NewService(database).PreviewAsync(Project.Id, settings, TestContext.Current.CancellationToken));

		return new SettingsImportOpening(settings, preview);
	}
}
