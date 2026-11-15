using System.Globalization;

using Pyxie.Data.Enums;

namespace Pyxie.Data.Services.Pipeline;

/// <summary>The contract between Pyxie and the agents it runs: where a card's workspace keeps the briefing and the result file, what its branch is called, the trailer that names it in the merge commit that lands it, the prompt every harness gets, and the words an agent reports its result with. The briefing describes it and the runtime follows it, so both read it from here.</summary>
public static class PipelineContract
{
	/// <summary>The folder in each card's workspace that holds Pyxie's files. It is excluded from git.</summary>
	public const string WorkspaceFolder = ".pyxie";

	/// <summary>The briefing file, relative to the workspace.</summary>
	public const string BriefingFile = ".pyxie/briefing.md";

	/// <summary>The result file the agent writes, relative to the workspace.</summary>
	public const string ResultFile = ".pyxie/result.json";

	/// <summary>The prefix of every card branch.</summary>
	public const string BranchPrefix = "pyxie/card-";

	/// <summary>The key of the git trailer that names the card in the merge commit Pyxie writes on the target branch, so what landed on a branch is read from structured data and not from the commit's subject.</summary>
	public const string CardTrailerKey = "Pyxie-Card";

	/// <summary>The prompt every harness is started with. It is fixed and short: the briefing carries the context.</summary>
	public const string Prompt = "Read .pyxie/briefing.md and carry out what it asks. Before you finish, write .pyxie/result.json exactly as it describes.";

	/// <summary>The word for a finished work round.</summary>
	public const string Completed = "completed";

	/// <summary>The word for approved work.</summary>
	public const string Approved = "approved";

	/// <summary>The word for rejected work.</summary>
	public const string Rejected = "rejected";

	/// <summary>The word for a merge whose conflicts were resolved.</summary>
	public const string Resolved = "resolved";

	/// <summary>The word for a round that broke but may succeed on another attempt.</summary>
	public const string Errored = "errored";

	/// <summary>The word for a round that cannot go on without a person.</summary>
	public const string Blocked = "blocked";

	/// <summary>Gets the branch a card works on.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The branch name, such as <c>pyxie/card-12</c>.</returns>
	public static string BranchName(int cardId)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{BranchPrefix}{cardId}");
	}

	/// <summary>Gets the trailer line that names a card in the merge commit that landed it. Git reads it as a trailer only as the last paragraph of the message, so it follows the subject after a blank line.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The line, such as <c>Pyxie-Card: 12</c>.</returns>
	public static string CardTrailer(int cardId)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{CardTrailerKey}: {cardId}");
	}

	/// <summary>Reads the card a branch belongs to.</summary>
	/// <param name="branchName">A branch name without <c>refs/heads/</c>.</param>
	/// <param name="cardId">The card identifier when the branch is a card branch; otherwise 0.</param>
	/// <returns><see langword="true"/> when <paramref name="branchName"/> is exactly a card branch name.</returns>
	public static bool TryParseBranchName(string branchName, out int cardId)
	{
		cardId = 0;
		if (!branchName.StartsWith(BranchPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		string number = branchName[BranchPrefix.Length..];
		if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0 || BranchName(parsed) != branchName)
		{
			return false;
		}

		cardId = parsed;

		return true;
	}

	/// <summary>Gets the words an agent may report for a round kind, in the order the briefing lists them.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns>The words, or none for rounds that run no agent.</returns>
	public static IReadOnlyList<string> OutcomeWords(RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work => [Completed, Errored, Blocked],
			RoundKind.Review => [Approved, Rejected, Errored, Blocked],
			RoundKind.Resolve => [Resolved, Errored, Blocked],
			_ => [],
		};
	}

	/// <summary>Reads the result an agent reported.</summary>
	/// <param name="kind">The kind of the round the agent ran.</param>
	/// <param name="word">The outcome word from the result file. Case and surrounding spaces are ignored.</param>
	/// <returns>The result, or <see langword="null"/> when the word is not one of <see cref="OutcomeWords"/> for the kind.</returns>
	public static RoundResult? ResultOf(RoundKind kind, string? word)
	{
		string normalized = word?.Trim() ?? string.Empty;
		string? match = OutcomeWords(kind).FirstOrDefault(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));

		return match switch
		{
			Completed or Approved or Resolved => RoundResult.Succeeded,
			Rejected => RoundResult.Failed,
			Errored => RoundResult.Errored,
			Blocked => RoundResult.Blocked,
			_ => null,
		};
	}
}
