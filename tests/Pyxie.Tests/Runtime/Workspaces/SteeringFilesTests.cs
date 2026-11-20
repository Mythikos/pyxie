using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

public sealed class SteeringFilesTests
{
	[Theory]
	[InlineData("CLAUDE.md")]
	[InlineData("CLAUDE.local.md")]
	[InlineData("AGENTS.md")]
	[InlineData("AGENTS.override.md")]
	[InlineData(".mcp.json")]
	[InlineData(".env")]
	[InlineData(".dsh/skills/reviewer/SKILL.md")]
	[InlineData("AGENTS.local.md")]
	[InlineData("src/app/CLAUDE.md")]
	[InlineData("docs/AGENTS.md")]
	[InlineData("claude.md")]
	[InlineData("Agents.MD")]
	[InlineData(".claude/settings.json")]
	[InlineData(".claude/commands/review.md")]
	[InlineData(".codex/config.toml")]
	[InlineData(".agents/skills/reviewer/SKILL.md")]
	[InlineData("packages/web/.claude/settings.local.json")]
	[InlineData(".Claude/settings.json")]
	[InlineData("/CLAUDE.md")]
	[InlineData(".claude//settings.json")]
	public void Contains_ForAHarnesssSettingsOrInstructionsAnywhere_IsTrue(string path)
	{
		Assert.True(SteeringFiles.Contains(path), $"{path} steers a harness.");
	}

	[Theory]
	[InlineData("")]
	[InlineData("/")]
	[InlineData("README.md")]
	[InlineData("src/app.cs")]
	[InlineData("CLAUDE.md.bak")]
	[InlineData("docs/CLAUDE.txt")]
	[InlineData("my.claude/settings.json")]
	[InlineData("claude/settings.json")]
	[InlineData("settings/.claudeignore")]
	[InlineData("mcp.json")]
	public void Contains_ForAnyOtherPath_IsFalse(string path)
	{
		Assert.False(SteeringFiles.Contains(path), $"{path} does not steer a harness.");
	}
}
