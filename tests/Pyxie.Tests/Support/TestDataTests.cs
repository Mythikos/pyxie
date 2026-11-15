using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>The helpers that arrange cards, agents, gates, and the repository mean what their documentation says, because every service test leans on them.</summary>
public sealed class TestDataTests
{
	[Fact]
	public async Task AddCardAsync_GivesEveryCardTheSameDefaultAgentAsWorkerThatCanBothWorkAndReview()
	{
		await using TestDatabase database = new();

		WorkCard first = await TestData.AddCardAsync(database, "First");
		WorkCard second = await TestData.AddCardAsync(database, "Second");

		Assert.NotNull(first.WorkerAgentId);
		Assert.Equal(first.WorkerAgentId, second.WorkerAgentId);
		Agent? agent = await TestData.FindAgentAsync(database, first.WorkerAgentId.Value);
		Assert.NotNull(agent);
		Assert.Equal(TestData.DefaultAgentName, agent.Name);
		Assert.True(agent.CanWork && agent.CanReview, "The default agent can fill either role.");
		Assert.Null(first.ReviewerAgentId);
	}

	[Fact]
	public async Task AddCardAsync_WithANullWorkerAddsACardWithoutOneAndCreatesNoDefaultAgent()
	{
		await using TestDatabase database = new();

		WorkCard card = await TestData.AddCardAsync(database, "Nobody", workerAgentId: null);

		Assert.Null(card.WorkerAgentId);
		Assert.Null((await TestData.FindCardAsync(database, card.Id))?.WorkerAgentId);
		Assert.Equal(0, await CountAgentsAsync(database));
	}

	[Fact]
	public async Task AddCardAsync_WithTheDefaultAgentAsReviewerUsesTheSameAgentAsTheWorker()
	{
		await using TestDatabase database = new();

		WorkCard card = await TestData.AddCardAsync(database, "Card", reviewerAgentId: TestData.DefaultAgent);

		Assert.NotNull(card.ReviewerAgentId);
		Assert.Equal(card.WorkerAgentId, card.ReviewerAgentId);
		Assert.Equal(1, await CountAgentsAsync(database));
	}

	[Fact]
	public async Task AddCardAsync_AnExplicitAgentIsUsedAsGivenAndNoDefaultAgentIsCreated()
	{
		await using TestDatabase database = new();
		Agent worker = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: false);

		WorkCard card = await TestData.AddCardAsync(database, "Card", workerAgentId: worker.Id);

		Assert.Equal(worker.Id, card.WorkerAgentId);
		Assert.Equal(1, await CountAgentsAsync(database));
	}

	[Fact]
	public async Task AddAgentAsync_MakesAnAgentThatDoesNotMergeUnlessAskedAndStoresTheFlagWhenAsked()
	{
		await using TestDatabase database = new();

		Agent plain = await TestData.AddAgentAsync(database, "Plain", canWork: true, canReview: true);
		Agent merger = await TestData.AddAgentAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);

		Assert.False((await TestData.FindAgentAsync(database, plain.Id))?.CanMerge, "An agent does not merge unless it is asked to.");
		Assert.True((await TestData.FindAgentAsync(database, merger.Id))?.CanMerge);
		await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddAgentAsync(database, "Second merger", canWork: false, canReview: false, canMerge: true));
	}

	[Fact]
	public async Task AddCardAsync_StoresTheStateAttemptsLimitAndResumeStageItIsGiven()
	{
		await using TestDatabase database = new();

		WorkCard card = await TestData.AddCardAsync(database, "Stopped", state: CardState.Error, attempt: 4, attemptLimit: 6, resumeRound: RoundKind.Merge);

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardState.Error, stored.State);
		Assert.Equal(4, stored.Attempt);
		Assert.Equal(6, stored.AttemptLimit);
		Assert.Equal(RoundKind.Merge, stored.ResumeRound);
	}

	[Fact]
	public async Task AddGateAsync_AppendsGatesInOrderEnabledUnlessSaidOtherwise()
	{
		await using TestDatabase database = new();

		Gate first = await TestData.AddGateAsync(database, "Build");
		Gate second = await TestData.AddGateAsync(database, "Lint", isEnabled: false);

		Assert.Equal([0, 1], new[] { first.SortOrder, second.SortOrder });
		Assert.Equal([true, false], new[] { first.IsEnabled, second.IsEnabled });
	}

	[Fact]
	public async Task ConnectRepository_InsertsAFolderImportWithAnAbsolutePathAnOriginAndTheMainBranch()
	{
		await using TestDatabase database = new();

		Repository repository = TestData.ConnectRepository(database);

		Assert.Equal(RepositorySource.Folder, repository.Source);
		Assert.Equal("main", repository.TargetBranch);
		Assert.True(Path.IsPathFullyQualified(repository.Path), $"'{repository.Path}' must be an absolute path.");
		Assert.True(Path.IsPathFullyQualified(repository.Origin), $"The origin '{repository.Origin}' must be a full path.");
		Assert.Equal(TestData.RepositoryPath, repository.Path);
	}

	[Fact]
	public async Task TestCardInputCreateAsync_ResolvesTheDefaultAgentButTheSyncCreateLeavesTheWorkerOut()
	{
		await using TestDatabase database = new();

		CardInput resolved = await TestCardInput.CreateAsync(database, "Card", reviewerAgentId: TestData.DefaultAgent);
		CardInput bare = TestCardInput.Create("Card");

		Assert.NotNull(resolved.WorkerAgentId);
		Assert.Equal(resolved.WorkerAgentId, resolved.ReviewerAgentId);
		Assert.Null(bare.WorkerAgentId);
		Assert.Null(bare.ReviewerAgentId);
	}

	private static async Task<int> CountAgentsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.CountAsync(TestContext.Current.CancellationToken);
	}
}
