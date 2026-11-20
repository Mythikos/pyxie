using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="AgentInputRules.Normalize"/> gives an agent as it is checked and stored: its name, command, and arguments trimmed, with the whitespace inside them and every other field kept.</summary>
public sealed class AgentInputRulesNormalizeTests
{
	[Theory]
	[InlineData(HarnessKind.Codex, true, false, false)]
	[InlineData(HarnessKind.DeepSeek, false, true, false)]
	[InlineData(HarnessKind.Other, false, false, true)]
	public void Normalize_TrimsTheNameCommandAndArguments_AndKeepsTheHarnessAndRoles(HarnessKind harness, bool canWork, bool canReview, bool canMerge)
	{
		AgentInput input = new(" \tClaude  Code\r\n", harness, "  claude ", "\n -p  --name \"{CARD_NAME}\" \t", canWork, canReview, canMerge);

		AgentInput normalized = AgentInputRules.Normalize(input);

		Assert.Equal(new AgentInput("Claude  Code", harness, "claude", "-p  --name \"{CARD_NAME}\"", canWork, canReview, canMerge), normalized);
	}

	[Fact]
	public void Normalize_OfANormalizedAgent_ChangesNothing()
	{
		AgentInput input = new("Claude", HarnessKind.ClaudeCode, "claude", "-p", CanWork: true, CanReview: true, CanMerge: false);

		Assert.Equal(input, AgentInputRules.Normalize(input));
	}

	[Fact]
	public void Normalize_OfBlankFields_LeavesThemEmpty()
	{
		AgentInput normalized = AgentInputRules.Normalize(new AgentInput("  ", HarnessKind.ClaudeCode, "\t", "\r\n", CanWork: true, CanReview: false));

		Assert.Equal(("", "", ""), (normalized.Name, normalized.Command, normalized.Arguments));
	}
}
