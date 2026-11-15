using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A stopped card resumes at the stage it stopped at. Editing its task (name, description, category, worker, or reviewer) clears that stage, so a changed task starts again at work; saving it unchanged, or with only a new attempt limit, keeps it.</summary>
public sealed class CardServiceResumeRoundTests
{
	private const string Description = "Stopped task";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static async Task<WorkCard> AddStoppedCardAsync(TestDatabase database, CardState state, bool withReviewer)
	{
		return await TestData.AddCardAsync(database, "Stopped", CardLane.Queue, reviewerAgentId: withReviewer ? TestData.DefaultAgent : null, description: Description, state: state, attempt: 3, resumeRound: RoundKind.Merge);
	}

	[Theory]
	[InlineData(CardState.Error, false)]
	[InlineData(CardState.Error, true)]
	[InlineData(CardState.Ready, false)]
	[InlineData(CardState.Ready, true)]
	public async Task UpdateAsync_SavingAStoppedCardUnchanged_KeepsTheStageItResumesAt(CardState state, bool withReviewer)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		WorkCard card = await AddStoppedCardAsync(database, state, withReviewer);
		CardInput unchanged = await TestCardInput.CreateAsync(database, card.Name, reviewerAgentId: withReviewer ? TestData.DefaultAgent : null, description: Description);

		ResultAssert.Succeeded(await service.UpdateAsync(card.Id, unchanged, Token));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(RoundKind.Merge, stored.ResumeRound);
		Assert.Equal((state, card.Version + 1), (stored.State, stored.Version));
	}

	[Fact]
	public async Task UpdateAsync_SavingAStoppedCardWithOnlyANewAttemptLimit_KeepsTheStageItResumesAtAndStoresTheLimit()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		WorkCard card = await AddStoppedCardAsync(database, CardState.Error, withReviewer: false);
		CardInput moreAttempts = await TestCardInput.CreateAsync(database, card.Name, description: Description, attemptLimit: card.AttemptLimit + 2);

		ResultAssert.Succeeded(await service.UpdateAsync(card.Id, moreAttempts, Token));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(RoundKind.Merge, stored.ResumeRound);
		Assert.Equal(card.AttemptLimit + 2, stored.AttemptLimit);
		Assert.Equal(CardState.Error, stored.State);
	}

	[Fact]
	public async Task UpdateAsync_ComparesTheTextAfterTrimmingIt_SoPaddingAloneKeepsTheStageItResumesAt()
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		WorkCard card = await AddStoppedCardAsync(database, CardState.Error, withReviewer: false);
		CardInput padded = await TestCardInput.CreateAsync(database, $"  {card.Name}  ", description: $"\t{Description}\n");

		ResultAssert.Succeeded(await service.UpdateAsync(card.Id, padded, Token));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(RoundKind.Merge, stored.ResumeRound);
		Assert.Equal((card.Name, Description), (stored.Name, stored.Description));
	}

	[Theory]
	[InlineData("name", false)]
	[InlineData("description", false)]
	[InlineData("category", false)]
	[InlineData("worker", false)]
	[InlineData("reviewer assigned", false)]
	[InlineData("reviewer removed", true)]
	[InlineData("reviewer replaced", true)]
	public async Task UpdateAsync_ChangingTheTaskOfAStoppedCard_ClearsTheStageItResumesAtSoItStartsAgainAtWork(string change, bool withReviewer)
	{
		await using TestDatabase database = new();
		CardService service = new(database.ContextFactory, database.Clock);
		WorkCard card = await AddStoppedCardAsync(database, CardState.Error, withReviewer);
		Agent other = await TestData.AddAgentAsync(database, "Other", canWork: true, canReview: true);
		int? reviewer = withReviewer ? TestData.DefaultAgent : null;

		CardInput changed = change switch
		{
			"name" => await TestCardInput.CreateAsync(database, "Renamed", reviewerAgentId: reviewer, description: Description),
			"description" => await TestCardInput.CreateAsync(database, card.Name, reviewerAgentId: reviewer, description: "A different task"),
			"category" => await TestCardInput.CreateAsync(database, card.Name, PyxieSeedData.BugCategoryId, reviewerAgentId: reviewer, description: Description),
			"worker" => await TestCardInput.CreateAsync(database, card.Name, workerAgentId: other.Id, reviewerAgentId: reviewer, description: Description),
			"reviewer assigned" or "reviewer replaced" => await TestCardInput.CreateAsync(database, card.Name, reviewerAgentId: other.Id, description: Description),
			"reviewer removed" => await TestCardInput.CreateAsync(database, card.Name, reviewerAgentId: null, description: Description),
			_ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown change."),
		};

		ResultAssert.Succeeded(await service.UpdateAsync(card.Id, changed, Token));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Null(stored.ResumeRound);
		Assert.Equal((CardState.Error, card.Version + 1), (stored.State, stored.Version));
	}
}
