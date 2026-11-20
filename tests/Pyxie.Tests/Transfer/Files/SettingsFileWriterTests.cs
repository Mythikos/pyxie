using System.Text;
using System.Text.Json;

using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary><see cref="SettingsFileWriter"/> writes the same bytes on every machine: UTF-8 without a byte order mark, LF line breaks, a two-space indent, a final line break, the file's own harness names, colors in upper case, and the characters Dockerfiles and arguments are full of written as they are. A section the settings do not hold is left out, and whatever is written reads back as it was.</summary>
public sealed class SettingsFileWriterTests
{
	private const string Golden = """
		{
		  "format": "pyxie.project-settings",
		  "version": 3,
		  "agents": [
		    {
		      "authentication": "account",
		      "name": "Claude",
		      "harness": "claudeCode",
		      "command": "claude",
		      "arguments": "-p --dangerously-skip-permissions --name \"{CARD_NAME}\"",
		      "canWork": true,
		      "canReview": true,
		      "canMerge": false
		    },
		    {
		      "authentication": "account",
		      "name": "Codex",
		      "harness": "codex",
		      "command": "codex",
		      "arguments": "exec --yolo -c 'model_reasoning_effort=high'",
		      "canWork": true,
		      "canReview": false,
		      "canMerge": true
		    },
		    {
		      "authentication": "apiKey",
		      "name": "DeepSeek",
		      "harness": "deepSeek",
		      "command": "dsh",
		      "arguments": "--profile headless",
		      "canWork": false,
		      "canReview": true,
		      "canMerge": false
		    },
		    {
		      "authentication": "none",
		      "name": "Script <local>",
		      "harness": "other",
		      "command": "/usr/local/bin/agent",
		      "arguments": "",
		      "canWork": false,
		      "canReview": false,
		      "canMerge": false
		    }
		  ],
		  "gates": [
		    {
		      "name": "Build",
		      "command": "dotnet",
		      "arguments": "build -warnaserror",
		      "timeoutMinutes": 30,
		      "enabled": true
		    },
		    {
		      "name": "Tests",
		      "command": "sh",
		      "arguments": "-c 'dotnet test && echo done'",
		      "timeoutMinutes": 45,
		      "enabled": true
		    },
		    {
		      "name": "Lint",
		      "command": "npm",
		      "arguments": "run lint",
		      "timeoutMinutes": 5,
		      "enabled": false
		    }
		  ],
		  "categories": [
		    {
		      "name": "Bug",
		      "color": "#FBE9EC"
		    },
		    {
		      "name": "Feature",
		      "color": "#EAF3E2"
		    },
		    {
		      "name": "Idée",
		      "color": "#1F3A93"
		    }
		  ],
		  "sandbox": {
		    "dockerfile": "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get update && apt-get install -y jq \\\n    && rm -rf /var/lib/apt/lists/*\n"
		  }
		}

		""";

	// Each field holds a value no other field of its item holds, so a field written under another's name fails the comparison.
	private static readonly ProjectSettings s_settings = new(
		[
			new AgentInput("Claude", HarnessKind.ClaudeCode, "claude", "-p --dangerously-skip-permissions --name \"{CARD_NAME}\"", CanWork: true, CanReview: true, CanMerge: false),
			new AgentInput("Codex", HarnessKind.Codex, "codex", "exec --yolo -c 'model_reasoning_effort=high'", CanWork: true, CanReview: false, CanMerge: true),
			new AgentInput("DeepSeek", HarnessKind.DeepSeek, "dsh", "--profile headless", CanWork: false, CanReview: true, CanMerge: false) { Authentication = AgentAuthentication.ApiKey },
			new AgentInput("Script <local>", HarnessKind.Other, "/usr/local/bin/agent", "", CanWork: false, CanReview: false, CanMerge: false),
		],
		[
			new GateInput("Build", "dotnet", "build -warnaserror", 30, IsEnabled: true),
			new GateInput("Tests", "sh", "-c 'dotnet test && echo done'", 45, IsEnabled: true),
			new GateInput("Lint", "npm", "run lint", 5, IsEnabled: false),
		],
		[
			new CategoryInput("Bug", HexColor.Parse("#fbe9ec")),
			new CategoryInput("Feature", CategoryColors.Green),
			new CategoryInput("Idée", new HexColor(0x1F, 0x3A, 0x93)),
		],
		new SandboxSettingsInput("ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get update && apt-get install -y jq \\\n    && rm -rf /var/lib/apt/lists/*\n"));

	public static TheoryData<HarnessKind> Harnesses { get; } = [.. Enum.GetValues<HarnessKind>()];

	public static TheoryData<TransferSection> Sections { get; } = [.. Enum.GetValues<TransferSection>()];

	[Fact]
	public void Write_WritesExactlyTheGoldenFile()
	{
		byte[] written = SettingsFileWriter.Write(s_settings);

		// The text first, for a readable difference; then the bytes, which also hold the encoding.
		Assert.Equal(Golden.ReplaceLineEndings("\n"), Encoding.UTF8.GetString(written));
		Assert.Equal(Encoding.UTF8.GetBytes(Golden.ReplaceLineEndings("\n")), written);
	}

	[Fact]
	public void Write_WritesNoByteOrderMarkNoCarriageReturnAndOneFinalLineBreak()
	{
		byte[] written = SettingsFileWriter.Write(s_settings);

		Assert.True(written[0] == (byte)'{', $"The file starts with its opening brace, with no byte order mark, but starts with 0x{written[0]:X2}.");
		Assert.False(written.Contains((byte)'\r'), "The file breaks its lines with LF only, on every machine.");
		Assert.True(written[^1] == (byte)'\n' && written[^2] == (byte)'}', "The file ends with its closing brace and one line break.");
	}

	[Theory]
	[MemberData(nameof(Sections))]
	public void Write_LeavesOutASectionTheSettingsDoNotHold(TransferSection section)
	{
		ProjectSettings settings = new(
			section == TransferSection.Agents ? null : s_settings.Agents,
			section == TransferSection.Gates ? null : s_settings.Gates,
			section == TransferSection.Categories ? null : s_settings.Categories,
			section == TransferSection.Sandbox ? null : s_settings.Sandbox);

		List<string> members = RootMembers(SettingsFileWriter.Write(settings));

		string leftOut = section switch
		{
			TransferSection.Agents => "agents",
			TransferSection.Gates => "gates",
			TransferSection.Categories => "categories",
			_ => "sandbox",
		};
		Assert.Equal(["format", "version", .. new[] { "agents", "gates", "categories", "sandbox" }.Where(member => member != leftOut)], members);
	}

	[Fact]
	public void Write_OfSectionsWithNoItems_WritesEmptyListsWhichAnImportTakesAsRemoveThemAll()
	{
		byte[] written = SettingsFileWriter.Write(new ProjectSettings([], [], [], null));

		Assert.Equal("{\n  \"format\": \"pyxie.project-settings\",\n  \"version\": 3,\n  \"agents\": [],\n  \"gates\": [],\n  \"categories\": []\n}\n", Encoding.UTF8.GetString(written));
	}

	[Fact]
	public void Write_ThenRead_GivesBackTheSettings()
	{
		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(SettingsFileWriter.Write(s_settings)));

		SettingsFileAssert.Same(s_settings, read);
	}

	[Theory]
	[MemberData(nameof(Harnesses))]
	public void Write_ThenRead_GivesBackEveryHarness(HarnessKind harness)
	{
		ProjectSettings settings = new([new AgentInput("Agent", harness, "agent", "--flag", CanWork: true, CanReview: false, CanMerge: true)], null, null, null);

		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(SettingsFileWriter.Write(settings)));

		SettingsFileAssert.Same(settings, read);
	}

	[Fact]
	public void Write_ThenRead_GivesBackEveryCharacterOfEveryValue()
	{
		const string awkward = "quote \" backslash \\ tab \t CR \r LF \n control \u0001 <tag> & 'single' é 😀 \u2028";
		ProjectSettings settings = new(
			[new AgentInput(awkward, HarnessKind.Other, awkward, awkward, CanWork: true, CanReview: true, CanMerge: false)],
			[new GateInput(awkward, awkward, awkward, 1, IsEnabled: true)],
			[new CategoryInput(awkward, new HexColor(0xAB, 0xCD, 0xEF))],
			new SandboxSettingsInput(awkward));

		ProjectSettings read = ResultAssert.Succeeded(SettingsFileReader.Read(SettingsFileWriter.Write(settings)));

		SettingsFileAssert.Same(settings, read);
	}

	[Fact]
	public void Write_OfAHarnessThisPyxieDoesNotHave_WritesItsNumber_WhichReadingRefusesByName()
	{
		ProjectSettings settings = new([new AgentInput("Future", (HarnessKind)42, "future", "", CanWork: true, CanReview: false)], null, null, null);

		byte[] written = SettingsFileWriter.Write(settings);

		Assert.Contains("\"harness\": \"42\"", Encoding.UTF8.GetString(written), StringComparison.Ordinal);
		Assert.Equal("Agent 'Future' has harness \"42\", which this Pyxie does not know. The harnesses are claudeCode, codex, deepSeek, and other.", ResultAssert.Failed(SettingsFileReader.Read(written)));
	}

	private static List<string> RootMembers(byte[] written)
	{
		using JsonDocument document = JsonDocument.Parse(written);

		return [.. document.RootElement.EnumerateObject().Select(member => member.Name)];
	}
}
