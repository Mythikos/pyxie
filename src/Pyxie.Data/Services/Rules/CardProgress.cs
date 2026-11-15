namespace Pyxie.Data.Services.Rules;

/// <summary>The facts about a card's run that decide what follows a round (see <see cref="PipelineRules.Next"/>).</summary>
/// <param name="Attempt">The attempt the card is on.</param>
/// <param name="AttemptLimit">The card's attempt limit.</param>
/// <param name="HasGates">Whether at least one gate is enabled; without one, gate rounds are skipped.</param>
/// <param name="HasReviewer">Whether the card has a reviewer; without one, review rounds are skipped.</param>
/// <param name="HasMerger">Whether the project has a merge agent; without one, a merge conflict goes back to the card's worker.</param>
/// <param name="ApprovedSinceLastWork">Whether the latest work, a worker's or a merge agent's, was already approved (or its review skipped), so passing the gates again leads straight to the merge.</param>
public sealed record CardProgress(int Attempt, int AttemptLimit, bool HasGates, bool HasReviewer, bool HasMerger, bool ApprovedSinceLastWork);
