using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

using static Pyxie.Tests.Transfer.Files.SettingsFileText;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>A file holds the sections it names: an absent or null section is not held and reads as <see langword="null"/>, which an import leaves alone, while an empty list is held with no items, which an import takes to mean remove them all. A file that holds no section is refused. The reader keeps values as written; trimming them is the import's.</summary>
public sealed class SettingsFileReaderSectionTests
{
	private const string NoSettings = "The settings file holds no settings.";

	[Fact]
	public void Read_OfAFileWithOneSection_LeavesTheAbsentSectionsNull()
	{
		ProjectSettings settings = ResultAssert.Succeeded(Read(FileWith(Section("gates", Gate("Build")))));

		Assert.Equal([TransferSection.Gates], settings.Sections);
		Assert.Null(settings.Agents);
		Assert.Null(settings.Categories);
		Assert.Null(settings.Sandbox);
	}

	[Fact]
	public void Read_OfSectionsWrittenAsNull_LeavesThemNull()
	{
		ProjectSettings settings = ResultAssert.Succeeded(Read(FileWith("\"agents\": null", "\"gates\": null", Section("categories", Category("Bug")), "\"sandbox\": null")));

		Assert.Equal([TransferSection.Categories], settings.Sections);
		Assert.Null(settings.Agents);
		Assert.Null(settings.Gates);
		Assert.Null(settings.Sandbox);
	}

	[Fact]
	public void Read_OfEmptyLists_HoldsThoseSectionsWithNoItems()
	{
		ProjectSettings settings = ResultAssert.Succeeded(Read(FileWith(Section("agents"), Section("gates"), Section("categories"))));

		Assert.Equal([TransferSection.Agents, TransferSection.Gates, TransferSection.Categories], settings.Sections);
		Assert.True(settings.Agents is { Count: 0 }, "An empty agents list is held with no agents, not left out.");
		Assert.True(settings.Gates is { Count: 0 }, "An empty gates list is held with no gates, not left out.");
		Assert.True(settings.Categories is { Count: 0 }, "An empty categories list is held with no categories, not left out.");
		Assert.Null(settings.Sandbox);
	}

	[Theory]
	[InlineData("{ \"format\": \"pyxie.project-settings\", \"version\": 1 }")]
	[InlineData("{ \"format\": \"pyxie.project-settings\", \"version\": 1, \"agents\": null, \"gates\": null, \"categories\": null, \"sandbox\": null }")]
	public void Read_OfAFileThatHoldsNoSection_RefusesIt(string text)
	{
		Assert.Equal(NoSettings, ResultAssert.Failed(Read(text)));
	}

	[Fact]
	public void Read_KeepsEveryValueAsWritten_LeavingTrimmingAndNormalizingToTheImport()
	{
		string text = FileWith(
			Section("agents", """{ "name": " Claude ", "harness": "codex", "command": " codex ", "arguments": " exec ", "canWork": false, "canReview": true, "canMerge": true }"""),
			Section("gates", """{ "name": " Build ", "command": " dotnet ", "arguments": " build ", "timeoutMinutes": 0, "enabled": false }"""),
			Section("categories", """{ "name": " Bug ", "color": "#000000" }"""),
			"""
			"sandbox": { "dockerfile": "\r\n  FROM node:22\r\n\r\n" }
			""");

		ProjectSettings settings = ResultAssert.Succeeded(Read(text));

		Assert.Equal([new AgentInput(" Claude ", HarnessKind.Codex, " codex ", " exec ", CanWork: false, CanReview: true, CanMerge: true)], settings.Agents);
		Assert.Equal([new GateInput(" Build ", " dotnet ", " build ", 0, IsEnabled: false)], settings.Gates);
		Assert.Equal([new CategoryInput(" Bug ", new HexColor(0, 0, 0))], settings.Categories);
		Assert.Equal(new SandboxSettingsInput("\r\n  FROM node:22\r\n\r\n"), settings.Sandbox);
	}

	[Fact]
	public void Read_OfMembersInAnyOrder_ReadsTheSameSettings()
	{
		string ordered = FileWith(Section("agents", Agent("Claude")), Section("categories", Category("Bug")));
		string shuffled = """{ "categories": [ { "color": "#FBE9EC", "name": "Bug" } ], "agents": [ { "canMerge": false, "canReview": true, "canWork": true, "arguments": "-p", "command": "claude", "harness": "claudeCode", "name": "Claude" } ], "version": 1, "format": "pyxie.project-settings" }""";

		SettingsFileAssert.Same(ResultAssert.Succeeded(Read(ordered)), ResultAssert.Succeeded(Read(shuffled)));
	}
}
