namespace Pyxie.Runtime.Git;

/// <summary>A commit as listed in history.</summary>
/// <param name="Sha">The full commit hash.</param>
/// <param name="ShortSha">The abbreviated hash git shows.</param>
/// <param name="ParentShas">The parent hashes; more than one for a merge commit.</param>
/// <param name="AuthorName">The author's name.</param>
/// <param name="AuthorEmail">The author's email address.</param>
/// <param name="AuthoredUtc">When it was authored, in UTC.</param>
/// <param name="Subject">The first line of the message.</param>
/// <param name="CardId">The card a Pyxie merge commit landed, from its <c>Pyxie-Card</c> trailer; <see langword="null"/> for every other commit, including Pyxie's merges of the target branch into a card's branch.</param>
public sealed record GitCommit(string Sha, string ShortSha, IReadOnlyList<string> ParentShas, string AuthorName, string AuthorEmail, DateTime AuthoredUtc, string Subject, int? CardId)
{
	/// <summary>Gets a value indicating whether this is one of Pyxie's merge commits: it has more than one parent and <see cref="PyxieIdentity"/> authored it. Her own part in a branch's history is merging, both the merge that lands a card and the merge of the target branch into a card's branch, and she writes each under her identity. A card's other commits hold an agent's work and carry the same identity, because the agent's sandbox is set up with it and Pyxie commits what an agent left uncommitted, so the author alone does not make a commit hers.</summary>
	public bool IsPyxieMerge => ParentShas.Count > 1 && AuthorName == PyxieIdentity.Name && AuthorEmail == PyxieIdentity.Email;
}
