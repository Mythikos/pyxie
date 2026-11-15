using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="AgentService"/> and the tokens in an agent's arguments: known tokens are stored as typed, never filled in, and a name in capitals between braces that is not a token is refused on create and on update, leaving what is stored as it was.</summary>
public sealed class AgentServiceArgumentTokenTests
{
	private const string KnownTokens = "-p --dangerously-skip-permissions --name \"{CARD_NAME}\" --branch {BRANCH} --stage {STAGE} --attempt {ATTEMPT} --id card-{CARD_ID}";
	private const string UnknownTokenMessage = "Arguments use {CARDNAME}, which is not a token Pyxie fills in. The tokens are {CARD_ID}, {CARD_NAME}, {BRANCH}, {STAGE}, and {ATTEMPT}.";

	private static AgentInput Input(string name, string arguments, HarnessKind harness = HarnessKind.ClaudeCode, string command = "claude")
	{
		return new AgentInput(name, harness, command, arguments, CanWork: true, CanReview: true);
	}

	[Fact]
	public async Task CreateAsync_WithKnownTokens_StoresTheArgumentsUnexpanded()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);

		Agent created = ResultAssert.Succeeded(await service.CreateAsync(Input("Claude", KnownTokens), TestContext.Current.CancellationToken));

		Assert.Equal(KnownTokens, created.Arguments);
		Agent? stored = await TestData.FindAgentAsync(database, created.Id);
		Assert.NotNull(stored);
		Assert.Equal(KnownTokens, stored.Arguments);
	}

	[Fact]
	public async Task CreateAsync_WithAnUnknownToken_FailsWithTheMessageAndStoresNoAgent()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		string message = ResultAssert.Failed(await service.CreateAsync(Input("Claude", "-p --name \"{CARDNAME}\""), token));

		Assert.Equal(UnknownTokenMessage, message);
		Assert.Empty(await service.ListAsync(token));
	}

	[Fact]
	public async Task CreateAsync_WithAnUnknownTokenBesideKnownOnes_StillFails()
	{
		await using TestDatabase database = new();
		AgentService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;

		string message = ResultAssert.Failed(await service.CreateAsync(Input("Claude", $"{KnownTokens} --note {{CARDNAME}}"), token));

		Assert.Equal(UnknownTokenMessage, message);
		Assert.Empty(await service.ListAsync(token));
	}

	[Fact]
	public async Task UpdateAsync_WithKnownTokens_StoresTheArgumentsUnexpanded()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true, arguments: "-p");
		AgentService service = new(database.ContextFactory, database.Clock);

		ResultAssert.Succeeded(await service.UpdateAsync(agent.Id, Input("Claude", KnownTokens), TestContext.Current.CancellationToken));

		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.Equal(KnownTokens, stored.Arguments);
	}

	[Fact]
	public async Task UpdateAsync_WithAnUnknownToken_FailsWithTheMessageAndLeavesTheStoredAgentUnchanged()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true, HarnessKind.ClaudeCode, "claude", "-p --name \"{CARD_NAME}\"");
		AgentService service = new(database.ContextFactory, database.Clock);

		string message = ResultAssert.Failed(await service.UpdateAsync(agent.Id, Input("Renamed", "-p --name \"{CARDNAME}\"", HarnessKind.Codex, "codex"), TestContext.Current.CancellationToken));

		Assert.Equal(UnknownTokenMessage, message);
		Agent? stored = await TestData.FindAgentAsync(database, agent.Id);
		Assert.NotNull(stored);
		Assert.Equal("Claude", stored.Name);
		Assert.Equal(HarnessKind.ClaudeCode, stored.Harness);
		Assert.Equal("claude", stored.Command);
		Assert.Equal("-p --name \"{CARD_NAME}\"", stored.Arguments);
	}
}
