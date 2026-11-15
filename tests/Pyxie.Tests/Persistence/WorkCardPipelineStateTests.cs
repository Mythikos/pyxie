using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>A card stores what the pipeline needs to carry on where it left off: its state, attempts, attempt limit, and the stage to resume at.</summary>
public sealed class WorkCardPipelineStateTests
{
	[Fact]
	public void NewCard_IsReadyWithTheDefaultAttemptLimitNoAttemptsAndNoResumeStage()
	{
		WorkCard card = new();

		Assert.Equal(CardState.Ready, card.State);
		Assert.Equal(PyxieLimits.DefaultAttemptLimit, card.AttemptLimit);
		Assert.Equal(0, card.Attempt);
		Assert.Null(card.ResumeRound);
	}

	[Theory]
	[InlineData(CardState.Ready, 3, 0, null)]
	[InlineData(CardState.Error, 3, 3, null)]
	[InlineData(CardState.Error, 7, 2, RoundKind.Gate)]
	[InlineData(CardState.Error, 1, 1, RoundKind.Review)]
	[InlineData(CardState.Error, 10, 4, RoundKind.Merge)]
	public async Task Card_StoresItsStateAttemptsLimitAndResumeStageAndReadsThemBack(CardState state, int limit, int attempt, RoundKind? resumeRound)
	{
		await using TestDatabase database = new();
		WorkCard added = await TestData.AddCardAsync(database, "Card", state: state, attempt: attempt, attemptLimit: limit, resumeRound: resumeRound);

		WorkCard? stored = await TestData.FindCardAsync(database, added.Id);

		Assert.NotNull(stored);
		Assert.Equal(state, stored.State);
		Assert.Equal(limit, stored.AttemptLimit);
		Assert.Equal(attempt, stored.Attempt);
		Assert.Equal(resumeRound, stored.ResumeRound);
	}

	[Fact]
	public async Task Card_AddedWithoutArrangingThemKeepsTheDefaults()
	{
		await using TestDatabase database = new();
		WorkCard added = await TestData.AddCardAsync(database, "Card");

		WorkCard? stored = await TestData.FindCardAsync(database, added.Id);

		Assert.NotNull(stored);
		Assert.Equal(CardState.Ready, stored.State);
		Assert.Equal(5, stored.AttemptLimit);
		Assert.Equal(0, stored.Attempt);
		Assert.Null(stored.ResumeRound);
	}
}
