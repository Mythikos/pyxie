using Pyxie.Data.Limits;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Tests.Support;

/// <summary>Builds valid <see cref="CardInput"/> values so a test names only the field it is about.</summary>
public static class TestCardInput
{
	/// <summary>The description used when a test does not supply one.</summary>
	public const string DefaultDescription = "Description";

	/// <summary>Creates card input without touching a database, so it has no worker unless one is given and the card service refuses it with "Worker is required.". Use it for input that is meant to fail on an earlier field, for a card that is refused before its fields are read, or with a worker the test already holds. Otherwise use <see cref="CreateAsync"/>.</summary>
	/// <param name="name">The card name.</param>
	/// <param name="categoryId">The category; the seeded Feature category unless given.</param>
	/// <param name="workerAgentId">The worker agent, if any.</param>
	/// <param name="reviewerAgentId">The reviewer agent, if any.</param>
	/// <param name="description">The card description; a non-blank placeholder unless given.</param>
	/// <param name="attemptLimit">The attempt limit; the default limit unless given.</param>
	/// <returns>The card input.</returns>
	public static CardInput Create(string name, int categoryId = PyxieSeedData.FeatureCategoryId, int? workerAgentId = null, int? reviewerAgentId = null, string description = DefaultDescription, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		return new CardInput(name, description, categoryId, workerAgentId, reviewerAgentId, attemptLimit);
	}

	/// <summary>Creates card input that passes validation unless an argument says otherwise, with the default agent as its worker (created in the database when it does not exist yet).</summary>
	/// <param name="database">The database that holds the agents.</param>
	/// <param name="name">The card name.</param>
	/// <param name="categoryId">The category; the seeded Feature category unless given.</param>
	/// <param name="workerAgentId">The worker agent: the default agent unless given, <see langword="null"/> for none.</param>
	/// <param name="reviewerAgentId">The reviewer agent: none unless given, <see cref="TestData.DefaultAgent"/> for the default agent.</param>
	/// <param name="description">The card description; a non-blank placeholder unless given.</param>
	/// <param name="attemptLimit">The attempt limit; the default limit unless given.</param>
	/// <returns>The card input.</returns>
	public static async Task<CardInput> CreateAsync(TestDatabase database, string name, int categoryId = PyxieSeedData.FeatureCategoryId, int? workerAgentId = TestData.DefaultAgent, int? reviewerAgentId = null, string description = DefaultDescription, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		return Create(name, categoryId, await TestData.ResolveAgentAsync(database, workerAgentId), await TestData.ResolveAgentAsync(database, reviewerAgentId), description, attemptLimit);
	}
}
