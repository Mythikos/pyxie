using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class AgentArgumentTokensSplitTests
{
	[Fact]
	public void Split_SeparatesTokensFromTheTextAroundThem()
	{
		IReadOnlyList<ArgumentTextPart> parts = AgentArgumentTokens.Split("claude -p --name \"{CARD_NAME}\" --branch={BRANCH}");

		Assert.Equal([new("claude -p --name \"", false), new("{CARD_NAME}", true), new("\" --branch=", false), new("{BRANCH}", true)], parts);
	}

	[Fact]
	public void Split_KeepsAdjacentTokensApart()
	{
		Assert.Equal([new("{CARD_ID}", true), new("{ATTEMPT}", true)], AgentArgumentTokens.Split("{CARD_ID}{ATTEMPT}"));
	}

	[Theory]
	[InlineData("echo ${HOME}")]
	[InlineData("--json {\"a\":1}")]
	[InlineData("--x {NOT_A_TOKEN}")]
	[InlineData("plain arguments")]
	public void Split_LeavesTextWithoutPyxiesTokensAsOnePlainPart(string text)
	{
		Assert.Equal([new ArgumentTextPart(text, false)], AgentArgumentTokens.Split(text));
	}

	[Fact]
	public void Split_GivesNoPartsForEmptyText()
	{
		Assert.Empty(AgentArgumentTokens.Split(string.Empty));
	}

	[Theory]
	[InlineData("claude -p --name {CARD_NAME} --stage {STAGE}")]
	[InlineData("${HOME}/{CARD_ID}")]
	[InlineData("{BRANCH}")]
	public void Split_PartsJoinBackIntoTheText(string text)
	{
		Assert.Equal(text, string.Concat(AgentArgumentTokens.Split(text).Select(part => part.Text)));
	}
}
