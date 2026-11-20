using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Planning;
using Pyxie.Tests.Transfer.Files;

using Xunit;

namespace Pyxie.Tests.Transfer.Planning;

/// <summary><see cref="SettingsRules.Normalize"/> gives a file's settings as the editors store them before they are checked, matched, or written: names, commands, and arguments trimmed, the Dockerfile with LF line breaks, no blank space around it, and one final line break. Everything else, the order of the items, and the sections a file does not hold stay as they are.</summary>
public sealed class SettingsRulesNormalizeTests
{
	[Fact]
	public void Normalize_TrimsAnAgentsNameCommandAndArguments_AndKeepsTheRest()
	{
		ProjectSettings settings = new([new AgentInput("  Claude Code \t", HarnessKind.DeepSeek, " \tclaude ", "\r\n-p  --name \"{CARD_NAME}\"  ", CanWork: false, CanReview: true, CanMerge: true)], null, null, null);

		ProjectSettings normalized = SettingsRules.Normalize(settings);

		Assert.Equal([new AgentInput("Claude Code", HarnessKind.DeepSeek, "claude", "-p  --name \"{CARD_NAME}\"", CanWork: false, CanReview: true, CanMerge: true)], normalized.Agents);
	}

	[Fact]
	public void Normalize_TrimsAGatesNameCommandAndArguments_AndKeepsTheRest()
	{
		ProjectSettings settings = new(null, [new GateInput(" Unit tests ", "\tdotnet\t", "  test  --no-build ", 45, IsEnabled: false)], null, null);

		ProjectSettings normalized = SettingsRules.Normalize(settings);

		Assert.Equal([new GateInput("Unit tests", "dotnet", "test  --no-build", 45, IsEnabled: false)], normalized.Gates);
	}

	[Fact]
	public void Normalize_TrimsACategorysName_AndKeepsItsColor()
	{
		HexColor color = new(0x1F, 0x3A, 0x93);
		ProjectSettings settings = new(null, null, [new CategoryInput("  Tech  debt ", color)], null);

		ProjectSettings normalized = SettingsRules.Normalize(settings);

		Assert.Equal([new CategoryInput("Tech  debt", color)], normalized.Categories);
	}

	[Theory]
	[InlineData("\r\n\r\n  ARG PYXIE_BASE\r\nFROM ${PYXIE_BASE}\r\n\r\nRUN npm ci  \r\n\r\n  ", "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\n\nRUN npm ci\n")]
	[InlineData("FROM ${PYXIE_BASE}", "FROM ${PYXIE_BASE}\n")]
	[InlineData("FROM ${PYXIE_BASE}\n", "FROM ${PYXIE_BASE}\n")]
	[InlineData(" \r\n\t", "")]
	[InlineData(null, "")]
	public void Normalize_GivesTheDockerfileAsTheSandboxEditorStoresIt(string? dockerfile, string expected)
	{
		ProjectSettings normalized = SettingsRules.Normalize(new ProjectSettings(null, null, null, new SandboxSettingsInput(dockerfile)));

		Assert.Equal(new SandboxSettingsInput(expected), normalized.Sandbox);
	}

	[Fact]
	public void Normalize_KeepsTheOrderOfTheItemsAndTheSectionsTheFileHolds()
	{
		ProjectSettings settings = new(null, [new GateInput("Lint", "npm", "run lint", 5, IsEnabled: true), new GateInput("Build", "dotnet", "build", 30, IsEnabled: true)], [], null);

		ProjectSettings normalized = SettingsRules.Normalize(settings);

		SettingsFileAssert.Same(settings, normalized);
	}
}
