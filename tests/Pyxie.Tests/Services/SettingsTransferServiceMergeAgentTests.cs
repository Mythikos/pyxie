using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An import of agents leaves the merge role exactly where the file puts it: on one agent or on none. The unique index on the role checks each row as it is written, so the role moves between agents, to a new agent, and away from a removed one without the index ever seeing two merge agents.</summary>
public sealed class SettingsTransferServiceMergeAgentTests
{
	private static async Task<string[]> MergeAgentsAsync(TestDatabase database)
	{
		return [.. (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Where(agent => agent.CanMerge).Select(agent => agent.Name)];
	}

	private static Task<ServiceResult> ImportAgentsAsync(TestDatabase database, params AgentInput[] agents)
	{
		return SettingsTransferServiceTestData.PreviewAndImportAsync(SettingsTransferServiceTestData.Create(database), new ProjectSettings(agents, null, null, null), SettingsTransferServiceTestData.Only(TransferSection.Agents));
	}

	// Both directions, so whichever row EF writes first, the role is never on two agents at once.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ImportAsync_MovesTheMergeRoleFromOneAgentToTheOther(bool firstHoldsIt)
	{
		await using TestDatabase database = new();
		Agent first = await TestData.AddAgentAsync(database, "First", canWork: true, canReview: true, canMerge: firstHoldsIt);
		Agent second = await TestData.AddAgentAsync(database, "Second", canWork: true, canReview: true, canMerge: !firstHoldsIt);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, AgentInput.From(first) with { CanMerge = !firstHoldsIt }, AgentInput.From(second) with { CanMerge = firstHoldsIt }));

		Assert.Equal([firstHoldsIt ? "Second" : "First"], await MergeAgentsAsync(database));
		Assert.Equal([first.Id, second.Id], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => agent.Id));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_GivesTheMergeRoleToANewAgentWhileTheHolderIsRemoved()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Holder", canWork: true, canReview: true, canMerge: true);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, SettingsTransferServiceTestData.Agent("Newcomer", canMerge: true)));

		Assert.Equal(["Newcomer"], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => agent.Name));
		Assert.Equal(["Newcomer"], await MergeAgentsAsync(database));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_GivesTheMergeRoleToANewAgentWhileTheHolderStaysWithoutIt()
	{
		await using TestDatabase database = new();
		Agent holder = await TestData.AddAgentAsync(database, "Holder", canWork: true, canReview: true, canMerge: true);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, AgentInput.From(holder) with { CanMerge = false }, SettingsTransferServiceTestData.Agent("Newcomer", canMerge: true)));

		Assert.Equal(["Newcomer"], await MergeAgentsAsync(database));
		Assert.Equal(holder.Id, (await SettingsTransferServiceTestData.FindAgentAsync(database, "Holder"))?.Id);
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_AFileWithNoMergeAgentClearsTheRole()
	{
		await using TestDatabase database = new();
		Agent holder = await TestData.AddAgentAsync(database, "Holder", canWork: true, canReview: true, canMerge: true);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: false);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, AgentInput.From(holder) with { CanMerge = false }, AgentInput.From(other)));

		Assert.Equal(0, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
		Assert.Equal(["Holder", "Other"], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => agent.Name));
	}

	[Fact]
	public async Task ImportAsync_LeavesTheMergeRoleOnAHolderTheFileKeepsItOn()
	{
		await using TestDatabase database = new();
		Agent holder = await TestData.AddAgentAsync(database, "Holder", canWork: true, canReview: true, canMerge: true);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: false);
		AgentInput changedHolder = AgentInput.From(holder) with { Command = "claude-next", Arguments = "-p {CARD_NAME}" };

		ResultAssert.Succeeded(await ImportAgentsAsync(database, changedHolder, AgentInput.From(other) with { Command = "other-next" }, SettingsTransferServiceTestData.Agent("Newcomer")));

		Assert.Equal(["Holder"], await MergeAgentsAsync(database));
		Agent? stored = await TestData.FindAgentAsync(database, holder.Id);
		Assert.Equal(changedHolder, stored is null ? null : AgentInput.From(stored));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_GivesTheMergeRoleWhenTheProjectHasNone()
	{
		await using TestDatabase database = new();
		Agent first = await TestData.AddAgentAsync(database, "First", canWork: true, canReview: true);
		Agent second = await TestData.AddAgentAsync(database, "Second", canWork: true, canReview: true);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, AgentInput.From(first), AgentInput.From(second) with { CanMerge = true }));

		Assert.Equal(["Second"], await MergeAgentsAsync(database));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_MovesTheMergeRoleBetweenAgentsTheFileRenamesByCase()
	{
		await using TestDatabase database = new();
		Agent holder = await TestData.AddAgentAsync(database, "holder", canWork: true, canReview: true, canMerge: true);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: true);

		ResultAssert.Succeeded(await ImportAgentsAsync(database, AgentInput.From(holder) with { Name = "Holder", CanMerge = false }, AgentInput.From(other) with { Name = "other", CanMerge = true }));

		Assert.Equal([(holder.Id, "Holder", false), (other.Id, "other", true)], (await SettingsTransferServiceTestData.ReadAgentsAsync(database)).Select(agent => (agent.Id, agent.Name, agent.CanMerge)));
		Assert.Equal(1, await SettingsTransferServiceTestData.CountMergeAgentsAsync(database));
	}

	[Fact]
	public async Task ImportAsync_LeavesTheMergeAgentAsItWasWhenOnlyOtherSectionsArePicked()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Holder", canWork: true, canReview: true, canMerge: true);
		SettingsTransferService service = SettingsTransferServiceTestData.Create(database);
		ProjectSettings settings = new([SettingsTransferServiceTestData.Agent("Holder"), SettingsTransferServiceTestData.Agent("Newcomer", canMerge: true)], [GateTestInput.Create("Build")], null, null);

		ResultAssert.Succeeded(await SettingsTransferServiceTestData.PreviewAndImportAsync(service, settings, SettingsTransferServiceTestData.Only(TransferSection.Gates)));

		Assert.Equal(["Holder"], await MergeAgentsAsync(database));
	}
}
