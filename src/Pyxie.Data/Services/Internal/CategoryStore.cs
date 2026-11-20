using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Persistence;

namespace Pyxie.Data.Services.Internal;

/// <summary>Owns the rule that keeps a category while cards use it, for <see cref="CategoryService"/> and the settings import alike. Every card counts, closed ones included, because a card's category is required (<c>Cards.CategoryId</c> is NOT NULL with a Restrict foreign key).</summary>
internal static class CategoryStore
{
	/// <summary>Counts the cards that use a category, in any lane.</summary>
	/// <param name="context">The context.</param>
	/// <param name="categoryId">The category.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The number of cards.</returns>
	public static Task<int> CountCardsAsync(ProjectDbContext context, int categoryId, CancellationToken cancellationToken)
	{
		return context.Cards.CountAsync(card => card.CategoryId == categoryId, cancellationToken);
	}

	/// <summary>Gets the message that refuses deleting a category that cards use.</summary>
	/// <param name="name">The category's name.</param>
	/// <param name="cards">The cards that use it.</param>
	/// <returns>The message.</returns>
	public static string InUseRefusal(string name, int cards)
	{
		return $"Category '{name}' is used by {cards} {(cards == 1 ? "card" : "cards")} and cannot be deleted.";
	}
}
