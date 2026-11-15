using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>The history a real hub ended up with after two cards landed on <c>main</c>, with Pyxie's identity on every commit the pipeline made: an agent's work commit on each card's branch, the merge of <c>main</c> into card 1's branch that brought in what card 2 landed, and the merge that lands each card with its <c>Pyxie-Card</c> trailer. Card 2 lands first, so card 1 has to catch up with <c>main</c> before it lands. The first commit is the test repository's own, by its configured person.</summary>
public static class LandedCardsHistory
{
	/// <summary>The branch of card 1.</summary>
	public const string Card1Branch = "pyxie/card-1";

	/// <summary>The subject of the work commit an agent made for card 1.</summary>
	public const string Card1Work = "Add an invert button";

	/// <summary>The subject of the merge of <c>main</c> into card 1's branch, which carries no trailer.</summary>
	public const string Card1CatchUp = "Merge main into pyxie/card-1";

	/// <summary>The subject of the merge that landed card 1 on <c>main</c>.</summary>
	public const string Card1Landing = "Merge pyxie/card-1: Add an invert button";

	/// <summary>The subject of the work commit an agent made for card 2.</summary>
	public const string Card2Work = "Add a step button";

	/// <summary>The subject of the merge that landed card 2 on <c>main</c>.</summary>
	public const string Card2Landing = "Merge pyxie/card-2: Add a step button";

	private const string Card2Branch = "pyxie/card-2";

	private static readonly DateTimeOffset s_start = new(2029, 12, 31, 20, 0, 0, TimeSpan.Zero);

	/// <summary>Adds the two cards to a new repository, ending with <c>main</c> checked out. <c>main</c>'s first-parent line is then, newest first: card 1's landing, card 2's landing, and the initial commit. Card 1's branch holds its work commit, then the catch-up merge, above the initial commit.</summary>
	/// <param name="repository">A repository that holds only its initial commit.</param>
	/// <returns>A task that completes when the history exists.</returns>
	public static async Task BuildAsync(TempGitRepository repository)
	{
		IReadOnlyDictionary<string, string> pyxie = PyxieIdentity.Environment;

		await repository.CreateBranchAsync(Card2Branch);
		await repository.CreateBranchAsync(Card1Branch);

		await repository.CheckoutAsync(Card2Branch);
		await DatedCommits.CommitFileAtAsync(repository, "step.txt", "step", s_start.AddMinutes(10), Card2Work, identity: pyxie);
		await repository.CheckoutAsync(TempGitRepository.MainBranch);
		await DatedCommits.MergeAtAsync(repository, Card2Branch, s_start.AddMinutes(20), $"{Card2Landing}\n\nPyxie-Card: 2", identity: pyxie);

		await repository.CheckoutAsync(Card1Branch);
		await DatedCommits.CommitFileAtAsync(repository, "invert.txt", "invert", s_start.AddMinutes(30), Card1Work, identity: pyxie);
		await DatedCommits.MergeAtAsync(repository, TempGitRepository.MainBranch, s_start.AddMinutes(40), Card1CatchUp, identity: pyxie);
		await repository.CheckoutAsync(TempGitRepository.MainBranch);
		await DatedCommits.MergeAtAsync(repository, Card1Branch, s_start.AddMinutes(50), $"{Card1Landing}\n\nPyxie-Card: 1", identity: pyxie);
	}
}
