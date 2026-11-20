using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Persistence;

public sealed class CardRoundPersistenceTests
{
	[Theory]
	[InlineData(HarnessKind.ClaudeCode)]
	[InlineData(HarnessKind.Codex)]
	[InlineData(HarnessKind.DeepSeek)]
	[InlineData(HarnessKind.Other)]
	public async Task Round_StoresTheAgentSnapshotInItsOwnRowAndReadsItBack(HarnessKind harness)
	{
		await using TestDatabase database = new();
		AgentAuthentication authentication = harness == HarnessKind.Other ? AgentAuthentication.None : AgentAuthentication.ApiKey;
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		await using (ProjectDbContext writer = database.CreateContext())
		{
			writer.Rounds.Add(new CardRound { CardId = card.Id, Sequence = 1, Kind = RoundKind.Work, Agent = new AgentSnapshot("Wanda", harness, "run.exe", "--fast -v", authentication), StartedUtc = DateTime.UtcNow });
			await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.NotNull(stored.Agent);
		Assert.Equal("Wanda", stored.Agent.Name);
		Assert.Equal(harness, stored.Agent.Harness);
		Assert.Equal(authentication, stored.Agent.Authentication);
		Assert.Equal("run.exe", stored.Agent.Command);
		Assert.Equal("--fast -v", stored.Agent.Arguments);
	}

	[Fact]
	public async Task Round_WithoutAnAgentReadsBackWithoutOne()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Gate, RoundOutcome.Passed);

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.Null(stored.Agent);
	}

	[Fact]
	public async Task Round_StoresAnAgentSnapshotWithEmptyArguments()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, outcome: null, "Wanda");

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.NotNull(stored.Agent);
		Assert.Equal(string.Empty, stored.Agent.Arguments);
	}

	[Fact]
	public async Task Round_ReadsItsTimestampsBackAsUtcAndARunningRoundHasNoOutcomeOrEnd()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, card.Id, 2, RoundKind.Gate, outcome: null);

		List<CardRound> rounds = await TestData.ReadRoundsAsync(database, card.Id);

		Assert.Equal(DateTimeKind.Utc, rounds[0].StartedUtc.Kind);
		Assert.Equal(DateTimeKind.Utc, rounds[0].EndedUtc?.Kind);
		Assert.Equal(RoundOutcome.Completed, rounds[0].Outcome);
		Assert.Equal(DateTimeKind.Utc, rounds[1].StartedUtc.Kind);
		Assert.Null(rounds[1].EndedUtc);
		Assert.Null(rounds[1].Outcome);
	}

	[Fact]
	public async Task Round_StoresEveryKindWithEveryOutcome()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		List<(RoundKind Kind, RoundOutcome? Outcome)> expected = [];
		int sequence = 0;
		foreach (RoundKind kind in Enum.GetValues<RoundKind>())
		{
			foreach (RoundOutcome outcome in Enum.GetValues<RoundOutcome>())
			{
				sequence++;
				await TestData.AddRoundAsync(database, card.Id, sequence, kind, outcome);
				expected.Add((kind, outcome));
			}
		}

		List<CardRound> rounds = await TestData.ReadRoundsAsync(database, card.Id);

		Assert.Equal(expected, rounds.Select(round => (round.Kind, round.Outcome)));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(10)]
	public async Task Round_StoresTheAttemptItBelongsTo(int attempt)
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda", attempt: attempt);

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.Equal(attempt, stored.Attempt);
	}

	[Fact]
	public async Task Round_KeepsLongBriefingsReportsAndNotesIntact()
	{
		await using TestDatabase database = new();
		WorkCard card = await TestData.AddCardAsync(database, "Card");
		string briefing = new('b', 59_000);
		string report = new('r', 99_000);
		string note = new('n', 1_000);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Review, RoundOutcome.Rejected, "Rita", note, report, briefing);

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.Equal(briefing, stored.Briefing);
		Assert.Equal(report, stored.Report);
		Assert.Equal(note, stored.Note);
	}

	[Fact]
	public async Task Database_RefusesTwoRoundsWithTheSameSequenceOnOneCardButAllowsItOnDifferentCards()
	{
		await using TestDatabase database = new();
		WorkCard first = await TestData.AddCardAsync(database, "First");
		WorkCard second = await TestData.AddCardAsync(database, "Second");
		await TestData.AddRoundAsync(database, first.Id, 1, RoundKind.Work, RoundOutcome.Completed);

		await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddRoundAsync(database, first.Id, 1, RoundKind.Gate, RoundOutcome.Passed));
		await TestData.AddRoundAsync(database, second.Id, 1, RoundKind.Work, RoundOutcome.Completed);

		Assert.Single(await TestData.ReadRoundsAsync(database, first.Id));
		Assert.Single(await TestData.ReadRoundsAsync(database, second.Id));
	}

	[Fact]
	public async Task Database_RefusesARoundForACardThatDoesNotExist()
	{
		await using TestDatabase database = new();

		await Assert.ThrowsAsync<DbUpdateException>(() => TestData.AddRoundAsync(database, 999, 1, RoundKind.Work, RoundOutcome.Completed));
	}

	[Fact]
	public async Task Database_DeletesTheRoundsOfADeletedCardAndOnlyThose()
	{
		await using TestDatabase database = new();
		WorkCard doomed = await TestData.AddCardAsync(database, "Doomed");
		WorkCard kept = await TestData.AddCardAsync(database, "Kept");
		await TestData.AddRoundAsync(database, doomed.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await TestData.AddRoundAsync(database, doomed.Id, 2, RoundKind.Gate, outcome: null);
		await TestData.AddRoundAsync(database, kept.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await using (ProjectDbContext context = database.CreateContext())
		{
			WorkCard card = await context.Cards.SingleAsync(existing => existing.Id == doomed.Id, TestContext.Current.CancellationToken);
			context.Cards.Remove(card);
			await context.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		Assert.Empty(await TestData.ReadRoundsAsync(database, doomed.Id));
		Assert.Single(await TestData.ReadRoundsAsync(database, kept.Id));
	}

	[Fact]
	public async Task Database_DoesNotDeleteACardsRoundsWhenAnAgentIsDeleted()
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Wanda", canWork: true, canReview: false);
		WorkCard card = await TestData.AddCardAsync(database, "Card", workerAgentId: agent.Id);
		await TestData.AddRoundAsync(database, card.Id, 1, RoundKind.Work, RoundOutcome.Completed, "Wanda");
		await using (ProjectDbContext context = database.CreateContext())
		{
			await context.Agents.Where(existing => existing.Id == agent.Id).ExecuteDeleteAsync(TestContext.Current.CancellationToken);
		}

		CardRound stored = Assert.Single(await TestData.ReadRoundsAsync(database, card.Id));

		Assert.Equal("Wanda", stored.Agent?.Name);
	}
}
