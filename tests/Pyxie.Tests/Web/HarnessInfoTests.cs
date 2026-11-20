using Pyxie.Data.Enums;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class HarnessInfoTests
{
	[Theory]
	[InlineData(HarnessKind.ClaudeCode, "-p --dangerously-skip-permissions")]
	[InlineData(HarnessKind.Codex, "exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check")]
	[InlineData(HarnessKind.DeepSeek, "--profile headless --permission-mode danger-full-access --approval-policy never")]
	[InlineData(HarnessKind.Other, "")]
	public void DefaultArguments_RunsEachHarnessNonInteractivelyAndLeavesOtherEmpty(HarnessKind harness, string expected)
	{
		Assert.Equal(expected, HarnessInfo.DefaultArguments(harness));
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode, "claude")]
	[InlineData(HarnessKind.Codex, "codex")]
	[InlineData(HarnessKind.DeepSeek, "dsh")]
	[InlineData(HarnessKind.Other, "")]
	public void DefaultCommand_NamesTheExecutableOfEachHarnessAndLeavesOtherEmpty(HarnessKind harness, string expected)
	{
		Assert.Equal(expected, HarnessInfo.DefaultCommand(harness));
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode, "Claude Code")]
	[InlineData(HarnessKind.Codex, "Codex")]
	[InlineData(HarnessKind.DeepSeek, "DeepSeek")]
	[InlineData(HarnessKind.Other, "Other")]
	public void DisplayName_IsTheUserFacingNameOfEachHarness(HarnessKind harness, string expected)
	{
		Assert.Equal(expected, HarnessInfo.DisplayName(harness));
	}

	[Fact]
	public void All_ListsEveryHarnessKindInDeclaredOrder()
	{
		Assert.Equal([HarnessKind.ClaudeCode, HarnessKind.Codex, HarnessKind.DeepSeek, HarnessKind.Other], HarnessInfo.All);
	}
}
