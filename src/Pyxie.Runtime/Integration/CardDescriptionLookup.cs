namespace Pyxie.Runtime.Integration;

/// <summary>Finds the description of a card, the task its work was asked to do. The integrator says what landed on the target branch with it and keeps no data services of its own, since it is shared by every round.</summary>
/// <param name="cardId">The card.</param>
/// <param name="cancellationToken">A token to cancel the lookup.</param>
/// <returns>The description, or <see langword="null"/> when the card is not known or has none.</returns>
public delegate Task<string?> CardDescriptionLookup(int cardId, CancellationToken cancellationToken);
