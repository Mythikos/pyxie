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
using Pyxie.Data.Services;
using Pyxie.Runtime.Sandboxes;
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

	private SandboxEditorHost(ServiceProvider services, GateInteractiveHost host, ScriptedSandboxes sandboxes, OpenProject openProject, string projectFolder)
	{
		_services = services;
		Host = host;
		Sandboxes = sandboxes;
		OpenProject = openProject;
		ProjectFolder = projectFolder;
	}

	/// <summary>Gets the interactive host the editor is rendered in.</summary>
	public GateInteractiveHost Host { get; }

	/// <summary>Gets the sandboxes that build the image, which the test scripts.</summary>
	public ScriptedSandboxes Sandboxes { get; }

	/// <summary>Gets the open project, which a test can close as another tab would.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets the open project's folder, private to the test and deleted with the host.</summary>
	public string ProjectFolder { get; }

	/// <summary>Renders the editor with a project open and waits until it has loaded the settings.</summary>
	/// <param name="database">The database the settings service reads and writes.</param>
	/// <param name="signIns">The sign-in status the editor shows; neither sign-in found unless given.</param>
	/// <returns>The host; the caller disposes it.</returns>
	public static async Task<SandboxEditorHost> StartAsync(TestDatabase database, SandboxSignInStatus? signIns = null)
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
		services.AddSingleton<ISandboxSignIns>(new FixedSignIns(signIns ?? NoSignIns));
		ServiceProvider provider = services.BuildServiceProvider();

		SandboxEditorHost editor = new(provider, new GateInteractiveHost(provider), sandboxes, openProject, projectFolder);
		try
		{
			await editor.Host.StartAsync<SandboxSettingsEditor>();
		}
		catch
		{
			await editor.DisposeAsync();
			throw;
		}

		return editor;
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

	/// <summary>Finds the Build button.</summary>
	/// <returns>The button.</returns>
	public Task<GateRenderedElement> BuildButtonAsync()
	{
		return Host.FindButtonAsync("Build");
	}

	/// <summary>Presses Save.</summary>
	/// <returns>A task that completes when the save has been handled and the editor rendered.</returns>
	public async Task SaveAsync()
	{
		await Host.ClickAsync(await SaveButtonAsync());
	}

	/// <summary>Presses Build.</summary>
	/// <returns>A task that completes when the build has ended and the editor rendered.</returns>
	public async Task BuildAsync()
	{
		await Host.ClickAsync(await BuildButtonAsync());
	}

	/// <summary>Reads the hover text of the wrapper around Build, which says why the button is disabled.</summary>
	/// <returns>The text, or <see langword="null"/> when there is none.</returns>
	public async Task<string?> BuildTitleAsync()
	{
		List<GateRenderedElement> elements = [.. await Host.ElementsAsync()];
		int build = elements.FindIndex(element => element.Name == "button" && element.Text == "Build");
		Assert.True(build > 0, "The editor should show Build inside its wrapper.");

		return elements.Take(build).Last(element => element.Name == "span").Attribute("title");
	}

	/// <summary>Reads what a successful build left next to the buttons.</summary>
	/// <returns>The status word, or <see langword="null"/> when no build succeeded since the Dockerfile was last saved.</returns>
	public async Task<string?> BuiltStatusAsync()
	{
		return (await Host.TextsWithClassAsync("sandbox-built")).SingleOrDefault();
	}

	/// <summary>Reads the tag of the image a successful build made.</summary>
	/// <returns>The tag, or <see langword="null"/> when none is shown.</returns>
	public async Task<string?> BuiltTagAsync()
	{
		return (await Host.TextsWithClassAsync("sandbox-tag")).SingleOrDefault();
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
	/// <returns>The HTML.</returns>
	public static async Task<string> RenderHtmlAsync(TestDatabase database, SandboxSignInStatus? signIns = null)
	{
		OpenProject openProject = new();
		openProject.Set(new Project { Id = 1, Name = "app", Folder = Path.Combine(Path.GetTempPath(), "pyxie-sbx-static"), IsOpen = true });

		await using ComponentHarness harness = new(services =>
		{
			services.AddSingleton(openProject);
			services.AddSingleton<ISandboxSettingsService>(new SandboxSettingsService(database.ContextFactory, database.Clock));
			services.AddSingleton<ISandboxes>(new ScriptedSandboxes());
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
	}
}
