using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Tests.Support;

/// <summary>Builds in-memory cards, with their category loaded, for tests of code that reads a card without a database.</summary>
public static class InMemoryCard
{
	/// <summary>Creates a card as the briefing builder expects to receive it.</summary>
	/// <param name="id">The card identifier.</param>
	/// <param name="name">The card name.</param>
	/// <param name="description">The card description.</param>
	/// <param name="categoryName">The category name.</param>
	/// <param name="attempt">The attempt the card is on; the briefing states it.</param>
	/// <param name="attemptLimit">The card's attempt limit; the briefing states it.</param>
	/// <returns>The card.</returns>
	public static WorkCard Create(int id = 7, string name = "Fix the login", string description = "Users cannot sign in with a plus sign in their address.", string categoryName = "Bug", int attempt = 1, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		return new WorkCard
		{
			Id = id,
			Name = name,
			Description = description,
			Attempt = attempt,
			AttemptLimit = attemptLimit,
			Category = new Category { Id = 1, Name = categoryName },
		};
	}
}
