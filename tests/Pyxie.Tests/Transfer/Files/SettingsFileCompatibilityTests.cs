using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>A version 1 settings file, as a Pyxie that writes version 1 exports it, keeps importing into every later Pyxie. The fixture and the settings it must read as are frozen: never edit them to make a test pass. A change to what a settings file holds or how it is encoded is a new file version (<see cref="SettingsFileFormat"/>), which adds its own fixture and test beside this one, and this one keeps passing unchanged.</summary>
public sealed class SettingsFileCompatibilityTests
{
	private const string Version1Fixture = """
		{
		  "format": "pyxie.project-settings",
		  "version": 1,
		  "agents": [
		    {
		      "name": "Claude",
		      "harness": "claudeCode",
		      "command": "claude",
		      "arguments": "-p --dangerously-skip-permissions --name \"{CARD_NAME}\"",
		      "canWork": true,
		      "canReview": true,
		      "canMerge": true
		    },
		    {
		      "name": "Codex",
		      "harness": "codex",
		      "command": "codex",
		      "arguments": "exec --yolo -c model_reasoning_effort=high",
		      "canWork": true,
		      "canReview": false,
		      "canMerge": false
		    },
		    {
		      "name": "OpenCode",
		      "harness": "openCode",
		      "command": "opencode",
		      "arguments": "run --title card-{CARD_ID}",
		      "canWork": false,
		      "canReview": true,
		      "canMerge": false
		    },
		    {
		      "name": "Review script",
		      "harness": "other",
		      "command": "/usr/local/bin/review",
		      "arguments": "",
		      "canWork": false,
		      "canReview": true,
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
		      "command": "dotnet",
		      "arguments": "test --no-build",
		      "timeoutMinutes": 60,
		      "enabled": true
		    },
		    {
		      "name": "Format",
		      "command": "sh",
		      "arguments": "-c 'dotnet format --verify-no-changes && echo formatted'",
		      "timeoutMinutes": 10,
		      "enabled": false
		    }
		  ],
		  "categories": [
		    {
		      "name": "Bug",
		      "color": "#FBE9EC"
		    },
		    {
		      "name": "Chore",
		      "color": "#EDEDE9"
		    },
		    {
		      "name": "Feature",
		      "color": "#EAF3E2"
		    },
		    {
		      "name": "Research",
		      "color": "#1F3A93"
		    }
		  ],
		  "sandbox": {
		    "dockerfile": "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\n\n# The .NET SDK for the gates.\nRUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /usr/share/dotnet \\\n    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet\nRUN npm install -g @anthropic-ai/claude-code@latest && npm cache clean --force\n"
		  }
		}

		""";

	private static readonly ProjectSettings s_version1Settings = new(
		[
			new AgentInput("Claude", HarnessKind.ClaudeCode, "claude", "-p --dangerously-skip-permissions --name \"{CARD_NAME}\"", CanWork: true, CanReview: true, CanMerge: true),
			new AgentInput("Codex", HarnessKind.Codex, "codex", "exec --yolo -c model_reasoning_effort=high", CanWork: true, CanReview: false, CanMerge: false),
			new AgentInput("OpenCode", HarnessKind.Other, "opencode", "run --title card-{CARD_ID}", CanWork: false, CanReview: true, CanMerge: false),
			new AgentInput("Review script", HarnessKind.Other, "/usr/local/bin/review", "", CanWork: false, CanReview: true, CanMerge: false),
		],
		[
			new GateInput("Build", "dotnet", "build -warnaserror", 30, IsEnabled: true),
			new GateInput("Tests", "dotnet", "test --no-build", 60, IsEnabled: true),
			new GateInput("Format", "sh", "-c 'dotnet format --verify-no-changes && echo formatted'", 10, IsEnabled: false),
		],
		[
			new CategoryInput("Bug", new HexColor(0xFB, 0xE9, 0xEC)),
			new CategoryInput("Chore", new HexColor(0xED, 0xED, 0xE9)),
			new CategoryInput("Feature", new HexColor(0xEA, 0xF3, 0xE2)),
			new CategoryInput("Research", new HexColor(0x1F, 0x3A, 0x93)),
		],
		new SandboxSettingsInput("ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\n\n# The .NET SDK for the gates.\nRUN curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir /usr/share/dotnet \\\n    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet\nRUN npm install -g @anthropic-ai/claude-code@latest && npm cache clean --force\n"));

	[Theory]
	[InlineData("deepSeekAccount", AgentAuthentication.Account)]
	[InlineData("deepSeekApiKey", AgentAuthentication.ApiKey)]
	public void Version2_ReadsBothDeepSeekChoicesAndEnforcesEachVersionsVocabulary(string token, AgentAuthentication authentication)
	{
		string file = $$"""
			{"format":"pyxie.project-settings","version":2,"agents":[{"name":"DeepSeek","harness":"{{token}}","command":"dsh","arguments":"--profile headless","canWork":true,"canReview":false,"canMerge":false}]}
			""";
		AgentInput imported = Assert.Single(ResultAssert.Succeeded(SettingsFileText.Read(file)).Agents ?? []);
		Assert.Equal(HarnessKind.DeepSeek, imported.Harness);
		Assert.Equal(authentication, imported.Authentication);
		Assert.False(SettingsFileText.Read(file.Replace("\"version\":2", "\"version\":1", StringComparison.Ordinal)).Succeeded, "Version 1 has no DeepSeek vocabulary.");
		Assert.False(SettingsFileText.Read(file.Replace(token, "openCode", StringComparison.Ordinal)).Succeeded, "The retired harness belongs only to version 1.");
	}

	[Theory]
	[InlineData("\n", "")]
	[InlineData("\r\n", "")]
	[InlineData("\r\n", "\uFEFF")]
	public void Read_OfTheVersion1Fixture_AsAnyEditorSavesIt_GivesItsSettings(string lineBreak, string byteOrderMark)
	{
		string file = byteOrderMark + Version1Fixture.ReplaceLineEndings(lineBreak);

		ProjectSettings read = ResultAssert.Succeeded(SettingsFileText.Read(file));

		SettingsFileAssert.Same(s_version1Settings, read);
	}
}
