using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>An agent or category can change between the moment the card service validates the references of a card and the moment it writes the card. The write is the first statement of a transaction that checks the references again, so the card is refused with the validator's message and nothing is written, instead of storing a worker that cannot work or letting a foreign-key exception escape. The other caller runs just before the service begins its transaction, after the first validation.</summary>
public sealed class CardServiceReferenceRaceTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static AgentInput Roles(string name, bool canWork, bool canReview)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, canWork, canReview);
	}

	[Fact]
	public async Task CreateAsync_RefusesAWorkerThatLosesTheWorkerRoleAfterTheFirstValidationAndStoresNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Worker", canWork: true, canReview: true);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.UpdateAsync(worker.Id, Roles("Worker", canWork: false, canReview: true), Token)));

		string message = ResultAssert.Failed(await rig.Cards.CreateAsync(TestCardInput.Create("Card", workerAgentId: worker.Id), Token));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Worker' cannot be assigned as a worker.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
		Assert.False((await TestData.FindAgentAsync(rig.Database, worker.Id))?.CanWork, "The agent's role change committed first.");
	}

	[Fact]
	public async Task CreateAsync_RefusesAReviewerThatLosesTheReviewerRoleAfterTheFirstValidationAndStoresNothing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Reviewer", canWork: true, canReview: true);
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: reviewer.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.UpdateAsync(reviewer.Id, Roles("Reviewer", canWork: true, canReview: false), Token)));

		string message = ResultAssert.Failed(await rig.Cards.CreateAsync(input, Token));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Reviewer' cannot be assigned as a reviewer.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
	}

	[Fact]
	public async Task CreateAsync_ReportsAWorkerDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent worker = await TestData.AddAgentAsync(rig.Database, "Worker", canWork: true, canReview: false);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.DeleteAsync(worker.Id, Token)));

		string message = ResultAssert.Failed(await rig.Cards.CreateAsync(TestCardInput.Create("Card", workerAgentId: worker.Id), Token));

		interceptor.AssertFired();
		Assert.Equal("Worker agent not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
	}

	[Fact]
	public async Task CreateAsync_ReportsAReviewerDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Reviewer", canWork: false, canReview: true);
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: reviewer.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.DeleteAsync(reviewer.Id, Token)));

		string message = ResultAssert.Failed(await rig.Cards.CreateAsync(input, Token));

		interceptor.AssertFired();
		Assert.Equal("Reviewer agent not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
	}

	[Fact]
	public async Task CreateAsync_ReportsACategoryDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		CategoryService categories = new(rig.Database.ContextFactory);
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Card", PyxieSeedData.ChoreCategoryId);
		interceptor.Arm(async () => ResultAssert.Succeeded(await categories.DeleteAsync(PyxieSeedData.ChoreCategoryId, Token)));

		string message = ResultAssert.Failed(await rig.Cards.CreateAsync(input, Token));

		interceptor.AssertFired();
		Assert.Equal("Category not found.", message);
		Assert.Equal(0, await TestData.CountCardsAsync(rig.Database));
	}

	[Fact]
	public async Task UpdateAsync_RefusesAWorkerThatLosesTheWorkerRoleAfterTheFirstValidationAndLeavesTheCardUntouched()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent current = await TestData.AddAgentAsync(rig.Database, "Current", canWork: true, canReview: false);
		Agent next = await TestData.AddAgentAsync(rig.Database, "Next", canWork: true, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card", current.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.UpdateAsync(next.Id, Roles("Next", canWork: false, canReview: true), Token)));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, TestCardInput.Create("Renamed", workerAgentId: next.Id), Token));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Next' cannot be assigned as a worker.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal((card.Name, current.Id, card.Version, card.UpdatedUtc), (stored.Name, stored.WorkerAgentId, stored.Version, stored.UpdatedUtc));
	}

	[Fact]
	public async Task UpdateAsync_RefusesAReviewerThatLosesTheReviewerRoleAfterTheFirstValidationAndLeavesTheCardUntouched()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Reviewer", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Card", reviewerAgentId: reviewer.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.UpdateAsync(reviewer.Id, Roles("Reviewer", canWork: false, canReview: false), Token)));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, input, Token));

		interceptor.AssertFired();
		Assert.Equal("Agent 'Reviewer' cannot be assigned as a reviewer.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Null(stored.ReviewerAgentId);
		Assert.Equal(card.Version, stored.Version);
	}

	[Fact]
	public async Task UpdateAsync_ReportsAWorkerDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent current = await TestData.AddAgentAsync(rig.Database, "Current", canWork: true, canReview: false);
		Agent next = await TestData.AddAgentAsync(rig.Database, "Next", canWork: true, canReview: false);
		WorkCard card = await rig.CreateCardAsync("Card", current.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.DeleteAsync(next.Id, Token)));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, TestCardInput.Create("Renamed", workerAgentId: next.Id), Token));

		interceptor.AssertFired();
		Assert.Equal("Worker agent not found.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal((card.Name, current.Id, card.Version), (stored.Name, stored.WorkerAgentId, stored.Version));
	}

	[Fact]
	public async Task UpdateAsync_ReportsAReviewerDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		Agent reviewer = await TestData.AddAgentAsync(rig.Database, "Reviewer", canWork: false, canReview: true);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Renamed", reviewerAgentId: reviewer.Id);
		interceptor.Arm(async () => ResultAssert.Succeeded(await rig.Agents.DeleteAsync(reviewer.Id, Token)));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, input, Token));

		interceptor.AssertFired();
		Assert.Equal("Reviewer agent not found.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal((card.Name, card.Version), (stored.Name, stored.Version));
	}

	[Fact]
	public async Task UpdateAsync_ReportsACategoryDeletedAfterTheFirstValidationAsNotFoundInsteadOfThrowing()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		CategoryService categories = new(rig.Database.ContextFactory);
		WorkCard card = await rig.CreateCardAsync("Card");
		CardInput input = await TestCardInput.CreateAsync(rig.Database, "Renamed", PyxieSeedData.ChoreCategoryId);
		interceptor.Arm(async () => ResultAssert.Succeeded(await categories.DeleteAsync(PyxieSeedData.ChoreCategoryId, Token)));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, input, Token));

		interceptor.AssertFired();
		Assert.Equal("Category not found.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal((card.Name, PyxieSeedData.FeatureCategoryId, card.Version), (stored.Name, stored.CategoryId, stored.Version));
	}
}
