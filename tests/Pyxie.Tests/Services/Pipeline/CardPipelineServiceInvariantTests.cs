using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>The guarantee the attempt limit exists for: however a stage keeps failing, a run makes at most <see cref="WorkCard.AttemptLimit"/> work rounds and as many review rounds, then the card stops and nothing claims it until a person retries it, which starts exactly one more bounded run.</summary>
public sealed class CardPipelineServiceInvariantTests
{
	public static TheoryData<RoundKind, RoundResult, int> FailingStages { get; } = BuildFailingStages();

	private static TheoryData<RoundKind, RoundResult, int> BuildFailingStages()
	{
		TheoryData<RoundKind, RoundResult, int> cases = new();
		(RoundKind Kind, RoundResult Result)[] stages =
		[
			(RoundKind.Work, RoundResult.Errored),
			(RoundKind.Gate, RoundResult.Failed),
			(RoundKind.Gate, RoundResult.Errored),
			(RoundKind.Review, RoundResult.Failed),
			(RoundKind.Review, RoundResult.Errored),
			(RoundKind.Merge, RoundResult.Failed),
			(RoundKind.Merge, RoundResult.Errored),
		];

		foreach ((RoundKind kind, RoundResult result) in stages)
		{
			foreach (int attemptLimit in new[] { 1, 2, 3 })
			{
				cases.Add(kind, result, attemptLimit);
			}
		}

		return cases;
	}

	private static int Count(IEnumerable<CardRound> rounds, RoundKind kind)
	{
		return rounds.Count(round => round.Kind == kind);
	}

	private static async Task AssertStoppedAsync(PipelineRig rig, WorkCard card)
	{
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Queue, stored.Lane);
		Assert.Equal(CardState.Error, stored.State);
		Assert.DoesNotContain(await TestData.ReadRoundsAsync(rig.Database, card.Id), round => round.Outcome is null);
		Assert.Null(await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public async Task AGateThatAlwaysFails_RunsExactlyAttemptLimitWorkRoundsAndStopsTheCardAndEachRetryStartsExactlyOneMoreRun(int attemptLimit)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: attemptLimit);
		Func<RoundKind, RoundResult> script = PipelineRunner.FailAt(RoundKind.Gate, RoundResult.Failed);

		int firstRun = await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterFirst = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(attemptLimit * 2, firstRun);
		Assert.Equal(attemptLimit, Count(afterFirst, RoundKind.Work));
		Assert.Equal(attemptLimit, Count(afterFirst, RoundKind.Gate));
		Assert.Equal(0, Count(afterFirst, RoundKind.Review));
		await AssertStoppedAsync(rig, card);

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));
		int secondRun = await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterSecond = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(attemptLimit * 2, secondRun);
		Assert.Equal(attemptLimit * 2, Count(afterSecond, RoundKind.Work));
		Assert.Equal(attemptLimit * 2, Count(afterSecond, RoundKind.Gate));
		await AssertStoppedAsync(rig, card);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public async Task AReviewerThatAlwaysRejects_RunsAtMostAttemptLimitWorkAndReviewRoundsAndStopsTheCardAndEachRetryStartsExactlyOneMoreRun(int attemptLimit)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: attemptLimit);
		Func<RoundKind, RoundResult> script = PipelineRunner.FailAt(RoundKind.Review, RoundResult.Failed);

		int firstRun = await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterFirst = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(attemptLimit * 3, firstRun);
		Assert.Equal(attemptLimit, Count(afterFirst, RoundKind.Work));
		Assert.Equal(attemptLimit, Count(afterFirst, RoundKind.Review));
		Assert.Equal(0, Count(afterFirst, RoundKind.Merge));
		await AssertStoppedAsync(rig, card);

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));
		int secondRun = await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterSecond = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.Equal(attemptLimit * 3, secondRun);
		Assert.Equal(attemptLimit * 2, Count(afterSecond, RoundKind.Work));
		Assert.Equal(attemptLimit * 2, Count(afterSecond, RoundKind.Review));
		await AssertStoppedAsync(rig, card);
	}

	[Theory]
	[MemberData(nameof(FailingStages))]
	public async Task AStageThatKeepsFailing_RunsExactlyAttemptLimitRoundsOfThatStageNeverMoreThanTheLimitOfAnyKindAndStopsUntilRetried(RoundKind failingKind, RoundResult failure, int attemptLimit)
	{
		await using PipelineRig rig = await PipelineArrangement.CreateRigWithGateAsync();
		WorkCard card = await rig.CreateCardAsync("Card", reviewerAgentId: TestData.DefaultAgent, attemptLimit: attemptLimit);
		Func<RoundKind, RoundResult> script = PipelineRunner.FailAt(failingKind, failure);

		await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterFirst = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		AssertRunIsBounded(afterFirst, failingKind, attemptLimit);
		await AssertStoppedAsync(rig, card);

		ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));
		await PipelineRunner.RunUntilIdleAsync(rig, script);

		List<CardRound> afterSecond = await TestData.ReadRoundsAsync(rig.Database, card.Id);
		Assert.True(afterSecond.Count > afterFirst.Count, "The retry should have started another run.");
		AssertRunIsBounded(afterSecond.Skip(afterFirst.Count), failingKind, attemptLimit);
		Assert.Equal(attemptLimit * 2, Count(afterSecond, failingKind));
		await AssertStoppedAsync(rig, card);
	}

	private static void AssertRunIsBounded(IEnumerable<CardRound> run, RoundKind failingKind, int attemptLimit)
	{
		List<CardRound> rounds = [.. run];

		Assert.Equal(attemptLimit, Count(rounds, failingKind));
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			Assert.True(Count(rounds, kind) <= attemptLimit, $"A run of a card limited to {attemptLimit} attempts ran {Count(rounds, kind)} {kind} rounds.");
		}

		Assert.True(rounds.Max(round => round.Attempt) <= attemptLimit, "No round may belong to an attempt beyond the limit.");
		Assert.True(rounds.All(round => round.Outcome is not null), "A stopped run leaves no round open.");
	}
}
