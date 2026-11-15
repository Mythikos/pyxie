using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class AgentFormModelTests
{
	private const string ClaudeArguments = "-p --dangerously-skip-permissions";
	private const string CodexArguments = "exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check";

	[Fact]
	public void CreateNew_StartsWithBothRolesSelectedTheMergeRoleClearedAndTheDefaultHarnessCommandAndArguments()
	{
		AgentFormModel model = AgentFormModel.CreateNew();

		Assert.True(model.CanWork, "A new agent should be able to work by default.");
		Assert.True(model.CanReview, "A new agent should be able to review by default.");
		Assert.False(model.CanMerge, "A new agent should not be the merge agent: only one agent can be, so it is a deliberate choice.");
		Assert.Equal(HarnessKind.ClaudeCode, model.Harness);
		Assert.Equal("claude", model.Command);
		Assert.Equal(ClaudeArguments, model.Arguments);
		Assert.Equal(string.Empty, model.Name);
		Assert.True(model.IsNew, "CreateNew should produce a form for a new agent.");
	}

	[Theory]
	[InlineData(true, true, true)]
	[InlineData(true, true, false)]
	[InlineData(true, false, true)]
	[InlineData(true, false, false)]
	[InlineData(false, true, true)]
	[InlineData(false, true, false)]
	[InlineData(false, false, true)]
	[InlineData(false, false, false)]
	public void ToInput_CarriesEachRoleFlagUnchanged(bool canWork, bool canReview, bool canMerge)
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Name = "Agent";
		model.CanWork = canWork;
		model.CanReview = canReview;
		model.CanMerge = canMerge;

		AgentInput input = model.ToInput();

		Assert.Equal(canWork, input.CanWork);
		Assert.Equal(canReview, input.CanReview);
		Assert.Equal(canMerge, input.CanMerge);
	}

	[Fact]
	public void ToInput_OfANewAgentLeftAloneIsNotTheMergeAgent()
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Name = "Agent";

		Assert.False(model.ToInput().CanMerge, "Saving a new agent without choosing the merge role should not make it the merge agent.");
	}

	[Theory]
	[InlineData(true, true, true)]
	[InlineData(true, true, false)]
	[InlineData(true, false, true)]
	[InlineData(true, false, false)]
	[InlineData(false, true, true)]
	[InlineData(false, true, false)]
	[InlineData(false, false, true)]
	[InlineData(false, false, false)]
	public void FromAgent_ReproducesTheStoredRolesInsteadOfTheNewAgentDefaults(bool canWork, bool canReview, bool canMerge)
	{
		Agent agent = new()
		{
			Name = "Stored",
			Harness = HarnessKind.Codex,
			Command = "codex",
			Arguments = "--fast",
			CanWork = canWork,
			CanReview = canReview,
			CanMerge = canMerge,
		};

		AgentFormModel model = AgentFormModel.FromAgent(agent);

		Assert.Equal("Stored", model.Name);
		Assert.Equal(HarnessKind.Codex, model.Harness);
		Assert.Equal("codex", model.Command);
		Assert.Equal("--fast", model.Arguments);
		Assert.Equal(canWork, model.CanWork);
		Assert.Equal(canReview, model.CanReview);
		Assert.Equal(canMerge, model.CanMerge);
		Assert.False(model.IsNew, "FromAgent should produce a form for a stored agent.");
	}

	[Fact]
	public void ToInput_TrimsTextFields()
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Name = "  Padded  ";
		model.Command = " run.exe ";
		model.Arguments = " -v ";

		AgentInput input = model.ToInput();

		Assert.Equal("Padded", input.Name);
		Assert.Equal("run.exe", input.Command);
		Assert.Equal("-v", input.Arguments);
	}

	[Theory]
	[InlineData(HarnessKind.ClaudeCode, HarnessKind.Codex, "codex", CodexArguments)]
	[InlineData(HarnessKind.Codex, HarnessKind.OpenCode, "opencode", "run")]
	[InlineData(HarnessKind.OpenCode, HarnessKind.Other, "", "")]
	[InlineData(HarnessKind.Other, HarnessKind.ClaudeCode, "claude", ClaudeArguments)]
	[InlineData(HarnessKind.ClaudeCode, HarnessKind.Other, "", "")]
	[InlineData(HarnessKind.Other, HarnessKind.Codex, "codex", CodexArguments)]
	public void ChangeHarness_SwitchesUntouchedCommandAndArgumentsToTheNewHarnessDefaults(HarnessKind from, HarnessKind to, string expectedCommand, string expectedArguments)
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.ChangeHarness(from);

		model.ChangeHarness(to);

		Assert.Equal(to, model.Harness);
		Assert.Equal(expectedCommand, model.Command);
		Assert.Equal(expectedArguments, model.Arguments);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ChangeHarness_ReplacesABlankCommandAndBlankArguments(string blank)
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Command = blank;
		model.Arguments = blank;

		model.ChangeHarness(HarnessKind.Codex);

		Assert.Equal("codex", model.Command);
		Assert.Equal(CodexArguments, model.Arguments);
	}

	[Fact]
	public void ChangeHarness_KeepsACommandAndArgumentsAPersonTyped()
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Command = "my-claude";
		model.Arguments = "-p --model sonnet";

		model.ChangeHarness(HarnessKind.Codex);

		Assert.Equal(HarnessKind.Codex, model.Harness);
		Assert.Equal("my-claude", model.Command);
		Assert.Equal("-p --model sonnet", model.Arguments);
	}

	[Fact]
	public void ChangeHarness_ReplacesTheDefaultCommandButKeepsCustomArguments()
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Arguments = "-p --model sonnet";

		model.ChangeHarness(HarnessKind.Codex);

		Assert.Equal("codex", model.Command);
		Assert.Equal("-p --model sonnet", model.Arguments);
	}

	[Fact]
	public void ChangeHarness_ReplacesTheDefaultArgumentsButKeepsACustomCommand()
	{
		AgentFormModel model = AgentFormModel.CreateNew();
		model.Command = "my-claude";

		model.ChangeHarness(HarnessKind.Codex);

		Assert.Equal("my-claude", model.Command);
		Assert.Equal(CodexArguments, model.Arguments);
	}

	[Fact]
	public void ChangeHarness_BackToTheFirstHarnessRestoresItsDefaults()
	{
		AgentFormModel model = AgentFormModel.CreateNew();

		model.ChangeHarness(HarnessKind.OpenCode);
		model.ChangeHarness(HarnessKind.ClaudeCode);

		Assert.Equal("claude", model.Command);
		Assert.Equal(ClaudeArguments, model.Arguments);
	}

	[Fact]
	public void ChangeHarness_LeavesAStoredAgentsCommandAndArgumentsAlone()
	{
		Agent agent = new()
		{
			Name = "Stored",
			Harness = HarnessKind.ClaudeCode,
			Command = "claude",
			Arguments = ClaudeArguments,
		};
		AgentFormModel model = AgentFormModel.FromAgent(agent);

		model.ChangeHarness(HarnessKind.Codex);

		Assert.Equal(HarnessKind.Codex, model.Harness);
		Assert.Equal("claude", model.Command);
		Assert.Equal(ClaudeArguments, model.Arguments);
	}
}
