using Pyxie.Data.Entities;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The sandbox settings editor shows the project's Dockerfile, saves it only when it changed, shows why a save was refused, and says whether each sign-in was found.</summary>
public sealed class SandboxSettingsEditorTests
{
	private const string Saved = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make\n";
	private const string Typed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make cmake\n";
	private const string ChangedElsewhere = "The sandbox settings were changed elsewhere. Reload them, then save again.";

	[Fact]
	public async Task Render_WithNothingSaved_ShowsTheDefaultDockerfile()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		Assert.Equal(SandboxDefaults.Dockerfile, await editor.DockerfileAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
	}

	[Fact]
	public async Task Render_ShowsTheSavedDockerfile()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved, expectedVersion: 0);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		Assert.Equal(Saved, await editor.DockerfileAsync());
	}

	[Fact]
	public async Task Save_IsEnabledOnlyWhileTheDockerfileDiffersFromTheSavedOne()
	{
		await using TestDatabase database = new();
		await SaveElsewhereAsync(database, Saved, expectedVersion: 0);
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		Assert.False((await editor.SaveButtonAsync()).IsClickable, "Nothing changed yet, so there is nothing to save.");

		await editor.TypeDockerfileAsync(Typed);
		Assert.True((await editor.SaveButtonAsync()).IsClickable, "A changed Dockerfile can be saved.");

		await editor.TypeDockerfileAsync("\r\n" + Saved.Replace("\n", "\r\n", StringComparison.Ordinal) + "   ");
		Assert.False((await editor.SaveButtonAsync()).IsClickable, "Other line endings and surrounding whitespace are not a change, because the save drops them.");
	}

	[Fact]
	public async Task Save_StoresTheDockerfileAndDisablesSaveUntilTheNextChange()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		await editor.TypeDockerfileAsync(Typed.Replace("\n", "\r\n", StringComparison.Ordinal));

		await editor.SaveAsync();

		SandboxSettings stored = await ReadAsync(database);
		Assert.Equal(Typed, stored.Dockerfile);
		Assert.Equal(1, stored.Version);
		Assert.Equal(Typed, await editor.DockerfileAsync());
		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.False((await editor.SaveButtonAsync()).IsClickable, "Once saved, there is nothing to save until the Dockerfile changes again.");
	}

	[Fact]
	public async Task Save_AgainAfterASave_LandsBecauseTheEditorKeepsTheVersionItSaved()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		await editor.TypeDockerfileAsync(Saved);
		await editor.SaveAsync();

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		Assert.Empty(await editor.Host.AlertsAsync());
		SandboxSettings stored = await ReadAsync(database);
		Assert.Equal(Typed, stored.Dockerfile);
		Assert.Equal(2, stored.Version);
	}

	[Fact]
	public async Task Save_AfterTheSettingsWereSavedElsewhere_ShowsWhyAndKeepsWhatWasTyped()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		await SaveElsewhereAsync(database, Saved, expectedVersion: 0);
		await editor.TypeDockerfileAsync(Typed);

		await editor.SaveAsync();

		Assert.Equal([ChangedElsewhere], await editor.Host.AlertsAsync());
		Assert.Equal(Typed, await editor.DockerfileAsync());
		Assert.True((await editor.SaveButtonAsync()).IsClickable, "The refused change is still unsaved.");
		SandboxSettings stored = await ReadAsync(database);
		Assert.Equal(Saved, stored.Dockerfile);
		Assert.Equal(1, stored.Version);
	}

	[Fact]
	public async Task Save_OfADockerfileThatDoesNotStartFromTheBaseImage_ShowsTheRuleSavesNothingAndClearsTheMessageOnceASaveLands()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);
		const string NotFromBase = "FROM node:22-bookworm-slim\n";
		await editor.TypeDockerfileAsync(NotFromBase);

		await editor.SaveAsync();

		string? rule = SandboxSettingsInputRules.ValidateDockerfile(NotFromBase);
		Assert.NotNull(rule);
		Assert.Equal([rule], await editor.Host.AlertsAsync());
		Assert.Equal(0, (await ReadAsync(database)).Version);

		await editor.TypeDockerfileAsync(Typed);
		await editor.SaveAsync();

		Assert.Empty(await editor.Host.AlertsAsync());
		Assert.Equal(Typed, (await ReadAsync(database)).Dockerfile);
	}

	[Fact]
	public async Task Render_ShowsTheDockerfileInAnEditorWithAVisibleLabelAndNoSpellchecking()
	{
		await using TestDatabase database = new();

		string html = await SandboxEditorHost.RenderHtmlAsync(database);

		Assert.Matches(@"<label for=""sandbox-dockerfile"" class=""sandbox-label""[^>]*>Dockerfile</label>", html);
		string field = GateHtml.OpenTag(html, "textarea", "id=\"sandbox-dockerfile\"");
		Assert.Equal("13", GateHtml.AttributeValue(field, "rows"));
		Assert.Equal("false", GateHtml.AttributeValue(field, "spellcheck"));
		Assert.Contains("sandbox-dockerfile", GateHtml.AttributeValue(field, "class")!.Split(' '));
	}

	[Fact]
	public async Task Render_OffersBuildAsASecondaryButtonAndSaveAsThePrimaryOne()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		Assert.Contains("ant-btn-color-default", (await editor.BuildButtonAsync()).Attribute("class")!.Split(' '));
		Assert.Contains("ant-btn-color-primary", (await editor.SaveButtonAsync()).Attribute("class")!.Split(' '));
	}

	[Fact]
	public async Task Render_BeforeAnyBuild_SaysNothingAboutAnImage()
	{
		await using TestDatabase database = new();
		await using SandboxEditorHost editor = await SandboxEditorHost.StartAsync(database);

		Assert.Null(await editor.BuiltStatusAsync());
		Assert.Null(await editor.BuiltTagAsync());
		Assert.Null(await editor.BuildLogAsync());
	}

	[Fact]
	public async Task Render_ListsTheSignInsUnderTheirOwnHeading()
	{
		await using TestDatabase database = new();

		string html = await SandboxEditorHost.RenderHtmlAsync(database);

		Assert.Matches(@"<h3 class=""sandbox-side-title""[^>]*>Sign-ins</h3>", html);
		Assert.Equal(["Claude Code account", "Claude Code API key", "Codex account", "Codex API key", "DeepSeek account", "DeepSeek API key"], SandboxEditorHost.ReadSignIns(html).Select(row => row.Name));
	}

	[Fact]
	public async Task Render_WithAllSignInsFound_SaysFoundForEachAndNothingElse()
	{
		await using TestDatabase database = new();
		SandboxSignInStatus found = SandboxEditorHost.AllSignIns;

		string html = await SandboxEditorHost.RenderHtmlAsync(database, found);

		Assert.Equal([("Claude Code account", "Found", (string?)null), ("Claude Code API key", "Found", (string?)null), ("Codex account", "Found", (string?)null), ("Codex API key", "Found", (string?)null), ("DeepSeek account", "Found", (string?)null), ("DeepSeek API key", "Found", (string?)null)], SandboxEditorHost.ReadSignIns(html));
	}

	[Fact]
	public async Task Render_WithNoSignInFound_SaysNotFoundAndHowToFixEachWithTheCommandAndWherePyxieLooks()
	{
		await using TestDatabase database = new();
		SandboxSignInStatus missing = SandboxEditorHost.NoSignIns;

		string html = await SandboxEditorHost.RenderHtmlAsync(database, missing);

		Assert.Equal(
			[
				("Claude Code account", "Not found", (string?)$"Run claude setup-token and save the token in {missing.ClaudeAccount.Path}."),
				("Claude Code API key", "Not found", (string?)$"Save your Anthropic API key in {missing.ClaudeApiKey.Path}."),
				("Codex account", "Not found", (string?)$"Run codex login with ChatGPT using file storage. Pyxie looks in {missing.CodexAccount.Path}."),
				("Codex API key", "Not found", (string?)$"Save your OpenAI API key in {missing.CodexApiKey.Path}."),
				("DeepSeek account", "Not found", (string?)$"Sign in with dsh web. Pyxie looks in {missing.DeepSeekAccount.Path}."),
				("DeepSeek API key", "Not found", (string?)$"Save your DeepSeek API key in {missing.DeepSeekApiKey.Path}."),
			],
			SandboxEditorHost.ReadSignIns(html));
	}

	[Fact]
	public async Task Render_WithOnlyCodexMissing_GivesAFixOnlyForCodex()
	{
		await using TestDatabase database = new();
		SandboxSignInStatus missingCodex = SandboxEditorHost.AllSignIns with { CodexAccount = SandboxEditorHost.NoSignIns.CodexAccount };

		string html = await SandboxEditorHost.RenderHtmlAsync(database, missingCodex);

		Assert.Equal([("Claude Code account", "Found", (string?)null), ("Claude Code API key", "Found", (string?)null), ("Codex account", "Not found", (string?)$"Run codex login with ChatGPT using file storage. Pyxie looks in {missingCodex.CodexAccount.Path}."), ("Codex API key", "Found", (string?)null), ("DeepSeek account", "Found", (string?)null), ("DeepSeek API key", "Found", (string?)null)], SandboxEditorHost.ReadSignIns(html));
	}

	private static async Task SaveElsewhereAsync(TestDatabase database, string dockerfile, int expectedVersion)
	{
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(dockerfile), expectedVersion, TestContext.Current.CancellationToken));
	}

	private static Task<SandboxSettings> ReadAsync(TestDatabase database)
	{
		return new SandboxSettingsService(database.ContextFactory, database.Clock).GetAsync(TestContext.Current.CancellationToken);
	}
}
