using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="AgentInput.From"/> gives every field a person sets on an agent, each in its own place, and nothing the database keeps for itself.</summary>
public sealed class AgentInputTests
{
	[Theory]
	[InlineData(HarnessKind.ClaudeCode, true, false, false)]
	[InlineData(HarnessKind.Codex, false, true, false)]
	[InlineData(HarnessKind.DeepSeek, false, false, true)]
	[InlineData(HarnessKind.Other, true, true, true)]
	public void From_GivesEveryFieldOfTheAgent(HarnessKind harness, bool canWork, bool canReview, bool canMerge)
	{
		Agent agent = new() { Id = 7, Name = "Claude", Harness = harness, Authentication = harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.ApiKey, Command = "claude", Arguments = "-p --name \"{CARD_NAME}\"", CanWork = canWork, CanReview = canReview, CanMerge = canMerge };

		AgentInput input = AgentInput.From(agent);

		Assert.Equal(new AgentInput("Claude", harness, "claude", "-p --name \"{CARD_NAME}\"", canWork, canReview, canMerge) { Authentication = harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.ApiKey }, input);
	}

	[Fact]
	public void From_GivesTheFieldsAsStored_WithoutTrimmingThem()
	{
		Agent agent = new() { Name = " Claude ", Command = " claude ", Arguments = " -p " };

		AgentInput input = AgentInput.From(agent);

		Assert.Equal((" Claude ", " claude ", " -p "), (input.Name, input.Command, input.Arguments));
	}
}
