using System.Net;
using System.Text.RegularExpressions;

using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

using RegexMatch = System.Text.RegularExpressions.Match;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The sandbox settings editor rendered interactively over the real settings service on a test database, with scripted sandboxes that build the image, the sign-in status the test chose, and an open project in a private folder of the test's own, where a build writes its log. A test types into the Dockerfile and presses Save and Build as a person would.</summary>
internal sealed class SandboxEditorHost : IAsyncDisposable
{
	/// <summary>The sign-in status of a machine where neither sign-in was found.</summary>
	public static readonly SandboxSignInStatus NoSignIns = new(@"C:\pyxie\secrets\claude-oauth-token", ClaudeTokenFound: false, @"C:\Users\person\.codex\auth.json", CodexSignInFound: false);

	private readonly ServiceProvider _services;
	private readonly List<string> _otherProjectFolders = [];

	private SandboxEditorHost(ServiceProvider services, GateInteractiveHost host, ScriptedSandboxes sandboxes, OpenProject openProject, string projectFolder)
	{
		_services = services;
		Host = host;
		Sandboxes = sandboxes;
		OpenProject = openProject;
		ProjectFolder = projectFolder;
	}

	/// <summary>Gets the interactive host the editor is rendered in; <see cref="ReopenAsync"/> replaces it.</summary>
	public GateInteractiveHost Host { get; private set; }

	/// <summary>Gets the sandboxes that build the image, which the test scripts.</summary>
	public ScriptedSandboxes Sandboxes { get; }

	/// <summary>Gets the image builds the editor shows and starts builds through, which a test can wait on.</summary>
	public SandboxImageBuilds ImageBuilds => _services.GetRequiredService<SandboxImageBuilds>();

	/// <summary>Gets the open project, which a test can close as another tab would.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets the open project's folder, private to the test and deleted with the host.</summary>
	public string ProjectFolder { get; }

	/// <summary>Gets the open project's key, which its images are found by.</summary>
	public string ProjectKey => SandboxNames.ProjectKey(ProjectFolder);

	/// <summary>Renders the editor with a project open and waits until it has loaded the settings and asked the engine for the image of the saved Dockerfile.</summary>
	/// <param name="database">The database the settings service reads and writes.</param>
	/// <param name="signIns">The sign-in status the editor shows; neither sign-in found unless given.</param>
	/// <param name="arrange">Sets up the engine before the editor is first rendered, as it was when Pyxie started, given the sandboxes and the open project's folder; nothing unless given.</param>
	/// <returns>The host; the caller disposes it.</returns>
	public static async Task<SandboxEditorHost> StartAsync(TestDatabase database, SandboxSignInStatus? signIns = null, Action<ScriptedSandboxes, string>? arrange = null)
	{
		string projectFolder = TempFolder.Create("pyxie-sbx-editor");
		OpenProject openProject = new();
		openProject.Set(new Project { Id = 1, Name = "app", Folder = projectFolder, IsOpen = true });
		ScriptedSandboxes sandboxes = new();

		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton<IJSRuntime, GateStubJsRuntime>();
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(openProject);
		services.AddSingleton<ISandboxSettingsService>(new SandboxSettingsService(database.ContextFactory, database.Clock));
		services.AddSingleton<ISandboxes>(sandboxes);
		services.AddSingleton(provider => new SandboxImageBuilds(provider.GetRequiredService<ISandboxes>(), database.Clock));
		services.AddSingleton<ISandboxSignIns>(new FixedSignIns(signIns ?? NoSignIns));
		ServiceProvider provider = services.BuildServiceProvider();

		SandboxEditorHost editor = new(provider, new GateInteractiveHost(provider), sandboxes, openProject, projectFolder);
		try
		{
			arrange?.Invoke(sandboxes, projectFolder);
			await editor.Host.StartAsync<SandboxSettingsEditor>();
		}
		catch
		{
			await editor.DisposeAsync();
			throw;
		}

		return editor;
	}

	/// <summary>Leaves the page and comes back to it: disposes what is rendered and renders a new editor over the same services, so over the same image builds, as going to another page and back does.</summary>
	/// <returns>A task that completes when the new editor has loaded the settings and the engine has answered its lookup of the image of the saved Dockerfile.</returns>
	public async Task ReopenAsync()
	{
		await Host.CloseAsync();
		Host = new GateInteractiveHost(_services);
		await Host.StartAsync<SandboxSettingsEditor>();
	}

	/// <summary>Makes the folder of another project, private to the test and deleted with the host, whose image a build can be for.</summary>
	/// <returns>The folder.</returns>
	public string NewOtherProjectFolder()
	{
		string folder = TempFolder.Create("pyxie-sbx-other");
		_otherProjectFolders.Add(folder);

		return folder;
	}

	/// <summary>Opens a project and comes back to the settings, as opening it on the Repository page and then going to Settings does: the open project changes and a new editor is rendered over the same image builds. Every project reads the test's one database, so they share the saved Dockerfile and differ only in their folder.</summary>
	/// <param name="project">The project to open.</param>
	/// <returns>A task that completes when the new editor has loaded the settings.</returns>
	public async Task SwitchProjectAsync(Project project)
	{
		OpenProject.Set(project);
		await ReopenAsync();
	}

	/// <summary>Finds the Dockerfile field.</summary>
	/// <returns>The field.</returns>
	public async Task<GateRenderedElement> DockerfileFieldAsync()
	{
		return Assert.Single(await Host.ElementsAsync(), element => element.Name == "textarea");
	}

	/// <summary>Reads the text the Dockerfile field shows.</summary>
	/// <returns>The text.</returns>
	public async Task<string> DockerfileAsync()
	{
		return (await DockerfileFieldAsync()).Attribute("value") ?? string.Empty;
	}

	/// <summary>Types a whole Dockerfile into the field, replacing what it showed.</summary>
	/// <param name="text">The new text.</param>
	/// <returns>A task that completes when the editor has handled the input and rendered.</returns>
	public async Task TypeDockerfileAsync(string text)
	{
		await Host.TypeAsync(await DockerfileFieldAsync(), text);
	}

	/// <summary>Finds the Save button.</summary>
	/// <returns>The button.</returns>
	public Task<GateRenderedElement> SaveButtonAsync()
	{
		return Host.FindButtonAsync("Save");
	}

	/// <summary>Finds the Build button, which is labelled Building while a build of the project's image runs or waits.</summary>
	/// <param name="label">What the button says: Build, or Building while a build runs.</param>
	/// <returns>The button.</returns>
	public Task<GateRenderedElement> BuildButtonAsync(string label = "Build")
	{
		return Host.FindButtonAsync(label);
	}

	/// <summary>Finds the Cancel button, which is shown only while a person's build of the project's image runs or waits.</summary>
	/// <returns>The button, or <see langword="null"/> when none is shown.</returns>
	public async Task<GateRenderedElement?> CancelButtonAsync()
	{
		return (await Host.FindElementsAsync("button", button => button.Text == "Cancel")).SingleOrDefault();
	}

	/// <summary>Presses Cancel, which stops a person's build of the project's image that runs or waits.</summary>
	/// <returns>A task that completes when the press has been handled and the editor rendered.</returns>
	public async Task CancelAsync()
	{
		GateRenderedElement cancel = await CancelButtonAsync() ?? throw new InvalidOperationException("The editor should show Cancel while a person's build of the project's image runs or waits.");
		await Host.ClickAsync(cancel);
	}

	/// <summary>Presses Save.</summary>
	/// <returns>A task that completes when the save has been handled and the editor rendered.</returns>
	public async Task SaveAsync()
	{
		await Host.ClickAsync(await SaveButtonAsync());
	}

	/// <summary>Presses Build, which starts a build in the background, and does not wait for it.</summary>
	/// <returns>A task that completes when the press has been handled and the editor rendered.</returns>
	public async Task StartBuildAsync()
	{
		await Host.ClickAsync(await BuildButtonAsync());
	}

	/// <summary>Presses Build and waits until the build has ended and the editor shows how it ended.</summary>
	/// <returns>A task that completes then. The test fails when the build does not end within <see cref="RuntimeTestTimeouts.Wait"/>.</returns>
	public async Task BuildAsync()
	{
		await StartBuildAsync();

		await WaitForAsync(ImageBuilds.Running);
	}

	/// <summary>Starts a build the way the pipeline does for a round whose image is not there yet, and does not wait for it. The build is of the Dockerfile the project starts with unless the test says otherwise.</summary>
	/// <param name="projectFolder">The folder of the project whose image is built; the open project's unless given.</param>
	/// <param name="dockerfile">The Dockerfile the round read; the default Dockerfile unless given.</param>
	/// <returns>The task of the build, which completes when it ends.</returns>
	public Task<ServiceResult<string>> PipelineBuildAsync(string? projectFolder = null, string? dockerfile = null)
	{
		return ImageBuilds.EnsureAsync(projectFolder ?? ProjectFolder, dockerfile ?? SandboxDefaults.Dockerfile, TestContext.Current.CancellationToken);
	}

	/// <summary>Waits for a build to end, however it ends.</summary>
	/// <param name="build">The task of the build: <see cref="SandboxImageBuilds.Running"/> for a person's build, or what <see cref="PipelineBuildAsync"/> returned.</param>
	/// <returns>A task that completes then. The test fails when the build does not end within <see cref="RuntimeTestTimeouts.Wait"/>.</returns>
	public static Task WaitForAsync(Task build)
	{
		// The editor renders the end of a build on the host's dispatcher, which runs what it is given in order, so the next read of the tree comes after that render.
		return build.WaitAsync(RuntimeTestTimeouts.Wait, TestContext.Current.CancellationToken);
	}

	/// <summary>Tells whether a button shows that it is loading, as Build does while a build of the project's image runs or waits.</summary>
	/// <param name="button">The button, read from the host after the last interaction.</param>
	/// <returns><see langword="true"/> when the button shows its spinner.</returns>
	public static bool IsLoading(GateRenderedElement button)
	{
		return (button.Attribute("class") ?? string.Empty).Split(' ').Contains("ant-btn-loading");
	}

	/// <summary>Reads the hover text of the wrapper around Build, which says why the button is disabled.</summary>
	/// <returns>The text, or <see langword="null"/> when there is none.</returns>
	public async Task<string?> BuildTitleAsync()
	{
		List<GateRenderedElement> elements = [.. await Host.ElementsAsync()];
		int build = elements.FindIndex(element => element.Name == "button" && element.Text is "Build" or "Building");
		Assert.True(build > 0, "The editor should show Build inside its wrapper.");

		return elements.Take(build).Last(element => element.Name == "span").Attribute("title");
	}

	/// <summary>Reads the Built status next to the buttons, which the editor shows while the engine has the image of the saved Dockerfile and no build of the project runs or waits.</summary>
	/// <returns>The status word, or <see langword="null"/> when the editor does not show it.</returns>
	public async Task<string?> BuiltStatusAsync()
	{
		return (await Host.TextsWithClassAsync("sandbox-built")).SingleOrDefault();
	}

	/// <summary>Reads the tag of the image the editor shows next to Built.</summary>
	/// <returns>The tag, or <see langword="null"/> when none is shown.</returns>
	public async Task<string?> BuiltTagAsync()
	{
		return (await Host.TextsWithClassAsync("sandbox-tag")).SingleOrDefault();
	}

	/// <summary>Fails the test when the editor says anything about the project's image or a failed build: Built, an image tag, an error, or the end of a log.</summary>
	/// <returns>A task that completes when the editor has been read.</returns>
	public async Task AssertNoOutcomeAsync()
	{
		Assert.Null(await BuiltStatusAsync());
		Assert.Null(await BuiltTagAsync());
		Assert.Empty(await Host.AlertsAsync());
		Assert.Null(await BuildLogAsync());
	}

	/// <summary>Reads the end of a failed build's log the editor shows.</summary>
	/// <returns>The log text, or <see langword="null"/> when none is shown.</returns>
	public async Task<string?> BuildLogAsync()
	{
		return (await Host.TextsWithClassAsync("sandbox-build-log")).SingleOrDefault();
	}

	/// <summary>Renders the editor to static HTML over the services of a test, which shows the parts of the page that never change after the first render. The interactive host cannot see them, because the compiler writes an element that holds only fixed text as markup.</summary>
	/// <param name="database">The database the settings service reads.</param>
	/// <param name="signIns">The sign-in status the editor shows; neither sign-in found unless given.</param>
	/// <param name="sandboxes">The sandboxes the editor would ask, which a test can read afterwards; new ones unless given.</param>
	/// <returns>The HTML.</returns>
	public static async Task<string> RenderHtmlAsync(TestDatabase database, SandboxSignInStatus? signIns = null, ScriptedSandboxes? sandboxes = null)
	{
		OpenProject openProject = new();
		openProject.Set(new Project { Id = 1, Name = "app", Folder = Path.Combine(Path.GetTempPath(), "pyxie-sbx-static"), IsOpen = true });

		await using ComponentHarness harness = new(services =>
		{
			services.AddSingleton(openProject);
			services.AddSingleton<ISandboxSettingsService>(new SandboxSettingsService(database.ContextFactory, database.Clock));
			services.AddSingleton<ISandboxes>(sandboxes ?? new ScriptedSandboxes());
			services.AddSingleton(provider => new SandboxImageBuilds(provider.GetRequiredService<ISandboxes>(), database.Clock));
			services.AddSingleton<ISandboxSignIns>(new FixedSignIns(signIns ?? NoSignIns));
		});

		return await (await harness.RenderAsync<SandboxSettingsEditor>()).HtmlAsync();
	}

	/// <summary>Reads the sign-in rows out of the HTML of the editor.</summary>
	/// <param name="html">The HTML <see cref="RenderHtmlAsync"/> returned.</param>
	/// <returns>The rows in order: the name of the sign-in, whether it says Found or Not found, and the fix under it, or <see langword="null"/> when the row has none.</returns>
	public static IReadOnlyList<(string Name, string Status, string? Fix)> ReadSignIns(string html)
	{
		List<(string Name, string Status, string? Fix)> rows = [];

		foreach (RegexMatch row in Regex.Matches(html, @"<li class=""signin""[^>]*>(.*?)</li>", RegexOptions.Singleline))
		{
			string inner = row.Groups[1].Value;
			RegexMatch name = Regex.Match(inner, @"<span class=""signin-name""[^>]*>(.*?)</span>", RegexOptions.Singleline);
			RegexMatch fix = Regex.Match(inner, @"<p class=""signin-fix""[^>]*>(.*?)</p>", RegexOptions.Singleline);
			string rest = fix.Success ? inner.Replace(fix.Value, string.Empty, StringComparison.Ordinal) : inner;
			string status = Regex.Replace(rest.Replace(name.Value, string.Empty, StringComparison.Ordinal), "<[^>]*>", string.Empty).Trim();
			rows.Add((WebUtility.HtmlDecode(name.Groups[1].Value), status, fix.Success ? WebUtility.HtmlDecode(fix.Groups[1].Value) : null));
		}

		Assert.NotEmpty(rows);

		return rows;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Host.CloseAsync();
		await _services.DisposeAsync();
		await TempFolder.DeleteAsync(ProjectFolder);

		foreach (string folder in _otherProjectFolders)
		{
			await TempFolder.DeleteAsync(folder);
		}
	}
}
