using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Persistence.Configuration;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Writes and reads rows directly through the context so service tests arrange and verify state independently of the services.</summary>
public static class TestData
{
	/// <summary>Stands in for "the default agent" in the worker and reviewer arguments of the helpers that add cards. <see langword="null"/> always means no agent, so a test that wants a card without a worker says so. The default agent is created on first use (see <see cref="EnsureDefaultAgentAsync"/>), can both work and review, and is the same agent every time.</summary>
	public const int DefaultAgent = 0;

	/// <summary>The name of the default agent. No test names an agent of its own this, so the default agent never collides with one.</summary>
	public const string DefaultAgentName = "Default agent";

	/// <summary>The hub path of the repository <see cref="ConnectRepository"/> connects. Nothing is ever written there.</summary>
	public static readonly string RepositoryPath = Path.Combine(Path.GetTempPath(), "pyxie-test-repository");

	/// <summary>Gets the agent every card gets by default, creating it when it does not exist yet. It can both work and review.</summary>
	/// <param name="database">The database to write to.</param>
	/// <returns>The stored default agent.</returns>
	public static async Task<Agent> EnsureDefaultAgentAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();
		Agent? existing = await context.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Name == DefaultAgentName, TestContext.Current.CancellationToken);

		return existing ?? await AddAgentAsync(database, DefaultAgentName, canWork: true, canReview: true);
	}

	/// <summary>Adds an agent.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The agent name.</param>
	/// <param name="canWork">Whether the agent may work.</param>
	/// <param name="canReview">Whether the agent may review.</param>
	/// <param name="harness">The harness; Claude Code unless given.</param>
	/// <param name="command">The command; <c>claude</c> unless given.</param>
	/// <param name="arguments">The arguments; none unless given.</param>
	/// <param name="canMerge">Whether the agent is the project's merge agent; no unless given. The database refuses a second merge agent.</param>
	/// <returns>The stored agent.</returns>
	public static async Task<Agent> AddAgentAsync(TestDatabase database, string name, bool canWork, bool canReview, HarnessKind harness = HarnessKind.ClaudeCode, string command = "claude", string arguments = "", bool canMerge = false)
	{
		await using ProjectDbContext context = database.CreateContext();
		Agent agent = new()
		{
			Name = name,
			Harness = harness,
			Command = command,
			Arguments = arguments,
			CanWork = canWork,
			CanReview = canReview,
			CanMerge = canMerge,
		};

		context.Agents.Add(agent);
		await context.SaveChangesAsync(TestContext.Current.CancellationToken);

		return agent;
	}

	/// <summary>Resolves the <see cref="DefaultAgent"/> stand-in to the default agent's identifier; any other value, including <see langword="null"/>, is returned as it is.</summary>
	/// <param name="database">The database holding the agents.</param>
	/// <param name="agentId">An agent identifier, <see cref="DefaultAgent"/>, or <see langword="null"/>.</param>
	/// <returns>The identifier to store.</returns>
	public static async Task<int?> ResolveAgentAsync(TestDatabase database, int? agentId)
	{
		return agentId == DefaultAgent ? (await EnsureDefaultAgentAsync(database)).Id : agentId;
	}

	/// <summary>Appends a card to the bottom of a lane exactly as arranged, without going through the services. A card added to the Closed lane is stamped as closed now.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The card name.</param>
	/// <param name="lane">The lane to append to.</param>
	/// <param name="categoryId">The category; the seeded Feature category unless given.</param>
	/// <param name="workerAgentId">The worker agent: the default agent unless given, <see langword="null"/> for none.</param>
	/// <param name="reviewerAgentId">The reviewer agent: none unless given, <see cref="DefaultAgent"/> for the default agent.</param>
	/// <param name="description">The card description; a non-blank placeholder unless given.</param>
	/// <param name="stampedUtc">The time to stamp as created and updated, and as closed for a closed card; the database clock's current time unless given.</param>
	/// <param name="state">The card's state; Ready unless given. Only a Queue card is ever in Error, but this helper does not insist.</param>
	/// <param name="attempt">The attempt the card is on; 0 (never claimed) unless given.</param>
	/// <param name="attemptLimit">The card's attempt limit; the default limit unless given.</param>
	/// <param name="resumeRound">The stage the pipeline resumes at when it claims the card from the Queue; none (start at work) unless given.</param>
	/// <returns>The stored card.</returns>
	public static async Task<WorkCard> AddCardAsync(TestDatabase database, string name, CardLane lane = CardLane.Queue, int categoryId = PyxieSeedData.FeatureCategoryId, int? workerAgentId = DefaultAgent, int? reviewerAgentId = null, string description = TestCardInput.DefaultDescription, DateTime? stampedUtc = null, CardState state = CardState.Ready, int attempt = 0, int attemptLimit = PyxieLimits.DefaultAttemptLimit, RoundKind? resumeRound = null)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		int? worker = await ResolveAgentAsync(database, workerAgentId);
		int? reviewer = await ResolveAgentAsync(database, reviewerAgentId);
		await using ProjectDbContext context = database.CreateContext();
		int count = await context.Cards.CountAsync(card => card.Lane == lane, cancellationToken);
		DateTime now = stampedUtc ?? database.Clock.UtcNow;
		WorkCard card = new()
		{
			Name = name,
			Description = description,
			Lane = lane,
			SortOrder = count,
			State = state,
			AttemptLimit = attemptLimit,
			Attempt = attempt,
			ResumeRound = resumeRound,
			CategoryId = categoryId,
			WorkerAgentId = worker,
			ReviewerAgentId = reviewer,
			CreatedUtc = now,
			UpdatedUtc = now,
			ClosedUtc = lane == CardLane.Closed ? now : null,
		};

		context.Cards.Add(card);
		await context.SaveChangesAsync(cancellationToken);

		return card;
	}

	/// <summary>Connects a repository by inserting its row directly, so the pipeline has somewhere to work. Nothing touches the disk: the path and the origin folder only have to look like paths.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="targetBranch">The branch finished cards are merged into; <c>main</c> unless given.</param>
	/// <returns>The stored repository, at <see cref="RepositoryPath"/>.</returns>
	/// <remarks>Synchronous so that <see cref="PipelineRig"/> can connect its repository from its constructor.</remarks>
	public static Repository ConnectRepository(TestDatabase database, string targetBranch = "main")
	{
		using ProjectDbContext context = database.CreateContext();
		Repository repository = new()
		{
			Source = RepositorySource.Folder,
			Path = RepositoryPath,
			Origin = Path.Combine(Path.GetTempPath(), "pyxie-test-source"),
			TargetBranch = targetBranch,
			ImportedUtc = database.Clock.UtcNow,
		};

		context.Repositories.Add(repository);
		context.SaveChanges();

		return repository;
	}

	/// <summary>Removes the repository row, leaving the project database as it is before its import connects the repository, so the pipeline claims nothing and records any round it is asked to open as blocked.</summary>
	/// <param name="database">The database to write to.</param>
	/// <returns>A task that completes when the repository is gone.</returns>
	public static async Task RemoveRepositoryAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();
		await context.Repositories.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Appends a gate to the end of the order gates run.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="name">The gate name; unique across gates.</param>
	/// <param name="isEnabled">Whether gate rounds run it. A gate round opens only while at least one gate is enabled.</param>
	/// <param name="command">The executable; <c>dotnet</c> unless given.</param>
	/// <param name="arguments">The arguments; <c>test</c> unless given.</param>
	/// <returns>The stored gate.</returns>
	public static async Task<Gate> AddGateAsync(TestDatabase database, string name = "Tests", bool isEnabled = true, string command = "dotnet", string arguments = "test")
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		await using ProjectDbContext context = database.CreateContext();
		Gate gate = new()
		{
			Name = name,
			Command = command,
			Arguments = arguments,
			SortOrder = await context.Gates.CountAsync(cancellationToken),
			IsEnabled = isEnabled,
		};

		context.Gates.Add(gate);
		await context.SaveChangesAsync(cancellationToken);

		return gate;
	}

	/// <summary>Appends a round to a card's history exactly as arranged, without going through the pipeline.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="cardId">The card the round belongs to.</param>
	/// <param name="sequence">The round's sequence in the card's history.</param>
	/// <param name="kind">The round kind.</param>
	/// <param name="outcome">How the round ended, or <see langword="null"/> for a running round.</param>
	/// <param name="agentName">The name of the agent snapshot, or <see langword="null"/> for a round without an agent.</param>
	/// <param name="note">The round note.</param>
	/// <param name="report">The round report.</param>
	/// <param name="briefing">The round briefing.</param>
	/// <param name="attempt">The attempt the round belongs to; 1 unless given.</param>
	/// <param name="startedUtc">When the round started; the database clock's time unless given.</param>
	/// <param name="endedUtc">When the round ended; the database clock's time for a round with an outcome and <see langword="null"/> for a running round, unless given.</param>
	/// <returns>The stored round.</returns>
	public static async Task<CardRound> AddRoundAsync(TestDatabase database, int cardId, int sequence, RoundKind kind, RoundOutcome? outcome, string? agentName = null, string? note = null, string? report = null, string? briefing = null, int attempt = 1, DateTime? startedUtc = null, DateTime? endedUtc = null)
	{
		await using ProjectDbContext context = database.CreateContext();
		DateTime now = database.Clock.UtcNow;
		CardRound round = new()
		{
			CardId = cardId,
			Sequence = sequence,
			Kind = kind,
			Attempt = attempt,
			Agent = agentName is null ? null : new AgentSnapshot(agentName, HarnessKind.ClaudeCode, "claude", string.Empty),
			Briefing = briefing,
			StartedUtc = startedUtc ?? now,
			EndedUtc = endedUtc ?? (outcome is null ? null : now),
			Outcome = outcome,
			Note = note,
			Report = report,
		};

		context.Rounds.Add(round);
		await context.SaveChangesAsync(TestContext.Current.CancellationToken);

		return round;
	}

	/// <summary>Reads a card's rounds without tracking, oldest first.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The rounds in sequence order.</returns>
	public static async Task<List<CardRound>> ReadRoundsAsync(TestDatabase database, int cardId)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Rounds
			.AsNoTracking()
			.Where(round => round.CardId == cardId)
			.OrderBy(round => round.Sequence)
			.ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Counts every stored round of every card.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The number of rounds.</returns>
	public static async Task<int> CountRoundsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Rounds.CountAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Appends cards to a lane in the order given. Each has the default worker and no reviewer.</summary>
	/// <param name="database">The database to write to.</param>
	/// <param name="lane">The lane to fill.</param>
	/// <param name="names">The card names, top to bottom.</param>
	/// <returns>The stored cards keyed by name.</returns>
	public static async Task<Dictionary<string, WorkCard>> AddLaneAsync(TestDatabase database, CardLane lane, params string[] names)
	{
		Dictionary<string, WorkCard> cards = [];
		foreach (string name in names)
		{
			cards[name] = await AddCardAsync(database, name, lane);
		}

		return cards;
	}

	/// <summary>Reads the card names of a lane, top to bottom, joined with commas.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="lane">The lane to read.</param>
	/// <returns>The names in sort order.</returns>
	public static async Task<string> ReadLaneNamesAsync(TestDatabase database, CardLane lane)
	{
		List<WorkCard> cards = await ReadLaneAsync(database, lane);

		return string.Join(",", cards.Select(card => card.Name));
	}

	/// <summary>Reads the sort orders of a lane, in sort order, joined with commas.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="lane">The lane to read.</param>
	/// <returns>The stored sort orders.</returns>
	public static async Task<string> ReadLaneSortOrdersAsync(TestDatabase database, CardLane lane)
	{
		List<WorkCard> cards = await ReadLaneAsync(database, lane);

		return string.Join(",", cards.Select(card => card.SortOrder));
	}

	/// <summary>Reads the cards of a lane ordered by sort order.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="lane">The lane to read.</param>
	/// <returns>The cards.</returns>
	public static async Task<List<WorkCard>> ReadLaneAsync(TestDatabase database, CardLane lane)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Cards
			.AsNoTracking()
			.Where(card => card.Lane == lane)
			.OrderBy(card => card.SortOrder)
			.ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads one card without tracking.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="id">The card identifier.</param>
	/// <returns>The card, or <see langword="null"/> when it does not exist.</returns>
	public static async Task<WorkCard?> FindCardAsync(TestDatabase database, int id)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Cards.AsNoTracking().SingleOrDefaultAsync(card => card.Id == id, TestContext.Current.CancellationToken);
	}

	/// <summary>Reads one agent without tracking.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="id">The agent identifier.</param>
	/// <returns>The agent, or <see langword="null"/> when it does not exist.</returns>
	public static async Task<Agent?> FindAgentAsync(TestDatabase database, int id)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Agents.AsNoTracking().SingleOrDefaultAsync(agent => agent.Id == id, TestContext.Current.CancellationToken);
	}

	/// <summary>Reads one category without tracking.</summary>
	/// <param name="database">The database to read from.</param>
	/// <param name="id">The category identifier.</param>
	/// <returns>The category, or <see langword="null"/> when it does not exist.</returns>
	public static async Task<Category?> FindCategoryAsync(TestDatabase database, int id)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Categories.AsNoTracking().SingleOrDefaultAsync(category => category.Id == id, TestContext.Current.CancellationToken);
	}

	/// <summary>Counts every stored card.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The number of cards.</returns>
	public static async Task<int> CountCardsAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Cards.CountAsync(TestContext.Current.CancellationToken);
	}
}
