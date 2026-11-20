using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Mcp;

internal sealed class CardTools(PipelineController pipeline, PipelineActivity activity, ICardService cards)
{
	[McpServerTool(ReadOnly = true), Description("Page through board cards in ID order with lane, sort order, assignments, attempts and history summaries. Pass nextAfterId as afterId until it is null.")]
	public async Task<object> GetBoardAsync(int projectId, CancellationToken cancellationToken, int afterId = 0, int count = 20)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (afterId < 0 || count is < 1 or > 100)
		{
			throw new McpException("Provide a nonnegative afterId and a count from 1 to 100.");
		}

		IReadOnlyList<BoardCardSummary> page = await cards.GetBoardPageAsync(afterId, count + 1, cancellationToken);
		return new { Cards = page.Take(count).Select(row => new { Card = Describe(row.Card), row.History }).ToArray(), NextAfterId = page.Count > count ? page[count - 1].Card.Id : (int?)null };
	}

	[McpServerTool(ReadOnly = true), Description("Inspect a card, including a closed card, and get its version for edits.")]
	public async Task<object> GetCardAsync(int projectId, int cardId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return Describe((await cards.GetAsync(cardId, cancellationToken)).Value());
	}

	[McpServerTool(ReadOnly = true), Description("Read a card's round history, briefings, reports and outcomes, starting at a one-based sequence number.")]
	public async Task<object> GetCardHistoryAsync(int projectId, int cardId, CancellationToken cancellationToken, int firstSequence = 1, int count = 10)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (firstSequence < 1 || count is < 1 or > 20)
		{
			throw new McpException("Use a positive sequence number and a count from 1 to 20.");
		}

		return (await cards.GetHistoryPageAsync(cardId, firstSequence, count, cancellationToken)).Value();
	}

	[McpServerTool(ReadOnly = true), Description("Find cards merged during a UTC interval, including subsequently closed cards. Start is inclusive and end is exclusive. Page with offset and count.")]
	public async Task<object> ListCompletedCardsAsync(int projectId, DateTimeOffset from, DateTimeOffset until, CancellationToken cancellationToken, int offset = 0, int count = 100)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (until <= from || offset < 0 || count is < 1 or > 200)
		{
			throw new McpException("Provide an increasing date range, a nonnegative offset and a count from 1 to 200.");
		}

		return await cards.GetCompletedAsync(from.UtcDateTime, until.UtcDateTime, offset, count, cancellationToken);
	}

	[McpServerTool, Description("Add a Queue card using category and worker IDs from get_project_settings. Omit the reviewer to skip review.")]
	public async Task<object> CreateCardAsync(int projectId, string name, string description, int categoryId, int workerAgentId, CancellationToken cancellationToken, int? reviewerAgentId = null, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		WorkCard card = (await cards.CreateAsync(new CardInput(name, description, categoryId, workerAgentId, reviewerAgentId, attemptLimit), cancellationToken)).Value();
		activity.NotifyCardsChanged();
		return Describe(card);
	}

	[McpServerTool, Description("Replace a Queue card's editable fields using its inspected version. Attachments are preserved.")]
	public async Task<object> UpdateCardAsync(int projectId, int cardId, int expectedVersion, string name, string description, int categoryId, int workerAgentId, int? reviewerAgentId, int attemptLimit, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		CardInput input = new(name, description, categoryId, workerAgentId, reviewerAgentId, attemptLimit) { ExpectedVersion = expectedVersion };
		WorkCard card = (await cards.UpdateAsync(cardId, input, cancellationToken)).Value();
		activity.NotifyCardsChanged();
		return Describe(card);
	}

	[McpServerTool(Destructive = true), Description("Delete a Queue card using its inspected version.")]
	public async Task<string> DeleteCardAsync(int projectId, int cardId, int expectedVersion, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return (await cards.DeleteAsync(cardId, cancellationToken, expectedVersion)).Changed(activity);
	}

	[McpServerTool, Description("Retry an errored Queue card with a fresh set of attempts.")]
	public async Task<string> RetryCardAsync(int projectId, int cardId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return (await cards.RetryAsync(cardId, cancellationToken)).Changed(activity);
	}

	[McpServerTool, Description("Close a Complete card and retain it in history.")]
	public async Task<string> CloseCardAsync(int projectId, int cardId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return (await cards.CloseAsync(cardId, cancellationToken)).Changed(activity);
	}

	private static object Describe(WorkCard card) => new { card.Id, card.Version, card.Name, card.Description, card.Lane, card.SortOrder, card.State, card.Attempt, card.AttemptLimit, card.CategoryId, Category = card.Category.Name, card.WorkerAgentId, Worker = card.WorkerAgent?.Name, card.ReviewerAgentId, Reviewer = card.ReviewerAgent?.Name, card.CreatedUtc, card.UpdatedUtc, card.ClosedUtc };

	[McpServerTool, Description("Close every currently Complete card in one operation and return the count. All card history is retained.")]
	public async Task<int> CloseCompletedCardsAsync(int projectId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		int closed = await cards.CloseCompletedAsync(cancellationToken);
		if (closed > 0)
		{
			activity.NotifyCardsChanged();
		}

		return closed;
	}
}
