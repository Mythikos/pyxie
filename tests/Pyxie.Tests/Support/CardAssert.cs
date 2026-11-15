using Pyxie.Data.Entities;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Assertions over stored cards that name the field that differs.</summary>
public static class CardAssert
{
	/// <summary>Asserts that a stored card still holds every value it was arranged with, including its position, lane, state, attempts, version, timestamps, and agents, and that no card was added or removed.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="original">The card as it was arranged.</param>
	/// <param name="expectedCardCount">The number of cards the database must still hold.</param>
	/// <returns>A task that completes when the assertions have run.</returns>
	public static async Task UnchangedAsync(TestDatabase database, WorkCard original, int expectedCardCount)
	{
		WorkCard? stored = await TestData.FindCardAsync(database, original.Id);
		Assert.True(stored is not null, $"Card '{original.Name}' no longer exists.");

		List<string> changes = [];
		Compare(changes, nameof(WorkCard.Name), original.Name, stored.Name);
		Compare(changes, nameof(WorkCard.Description), original.Description, stored.Description);
		Compare(changes, nameof(WorkCard.Lane), original.Lane, stored.Lane);
		Compare(changes, nameof(WorkCard.SortOrder), original.SortOrder, stored.SortOrder);
		Compare(changes, nameof(WorkCard.State), original.State, stored.State);
		Compare(changes, nameof(WorkCard.AttemptLimit), original.AttemptLimit, stored.AttemptLimit);
		Compare(changes, nameof(WorkCard.Attempt), original.Attempt, stored.Attempt);
		Compare(changes, nameof(WorkCard.ResumeRound), original.ResumeRound, stored.ResumeRound);
		Compare(changes, nameof(WorkCard.Version), original.Version, stored.Version);
		Compare(changes, nameof(WorkCard.CategoryId), original.CategoryId, stored.CategoryId);
		Compare(changes, nameof(WorkCard.WorkerAgentId), original.WorkerAgentId, stored.WorkerAgentId);
		Compare(changes, nameof(WorkCard.ReviewerAgentId), original.ReviewerAgentId, stored.ReviewerAgentId);
		Compare(changes, nameof(WorkCard.CreatedUtc), original.CreatedUtc, stored.CreatedUtc);
		Compare(changes, nameof(WorkCard.UpdatedUtc), original.UpdatedUtc, stored.UpdatedUtc);
		Compare(changes, nameof(WorkCard.ClosedUtc), original.ClosedUtc, stored.ClosedUtc);

		Assert.True(changes.Count == 0, $"Card '{original.Name}' was meant to be left untouched but: {string.Join("; ", changes)}.");
		Assert.True(await TestData.CountCardsAsync(database) == expectedCardCount, $"The database should still hold {expectedCardCount} cards.");
	}

	private static void Compare<TValue>(List<string> changes, string field, TValue expected, TValue actual)
	{
		if (!EqualityComparer<TValue>.Default.Equals(expected, actual))
		{
			changes.Add($"{field} changed from '{expected}' to '{actual}'");
		}
	}
}
