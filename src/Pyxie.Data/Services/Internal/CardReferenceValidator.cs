using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Data.Services.Internal;

/// <summary>Checks that the category and agents a card refers to exist and fit their roles.</summary>
internal static class CardReferenceValidator
{
	/// <summary>Validates the category of a card, its worker, and its optional reviewer.</summary>
	/// <param name="context">The context to query.</param>
	/// <param name="input">The card fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the references are valid.</returns>
	public static async Task<string?> ValidateAsync(ProjectDbContext context, CardInput input, CancellationToken cancellationToken)
	{
		bool categoryExists = await context.Categories.AsNoTracking().AnyAsync(category => category.Id == input.CategoryId, cancellationToken);
		if (!categoryExists)
		{
			return "Category not found.";
		}

		if (input.WorkerAgentId is int workerId)
		{
			Agent? worker = await context.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == workerId, cancellationToken);
			if (worker is null)
			{
				return "Worker agent not found.";
			}

			if (!worker.CanWork)
			{
				return $"Agent '{worker.Name}' cannot be assigned as a worker.";
			}
		}

		if (input.ReviewerAgentId is int reviewerId)
		{
			Agent? reviewer = await context.Agents.AsNoTracking().FirstOrDefaultAsync(agent => agent.Id == reviewerId, cancellationToken);
			if (reviewer is null)
			{
				return "Reviewer agent not found.";
			}

			if (!reviewer.CanReview)
			{
				return $"Agent '{reviewer.Name}' cannot be assigned as a reviewer.";
			}
		}

		return null;
	}
}
