namespace Pyxie.Runtime.Git;

/// <summary>A commit as listed in history.</summary>
/// <param name="Sha">The full commit hash.</param>
/// <param name="ShortSha">The abbreviated hash git shows.</param>
/// <param name="ParentShas">The parent hashes; more than one for a merge commit.</param>
/// <param name="AuthorName">The author's name.</param>
/// <param name="AuthorEmail">The author's email address.</param>
/// <param name="AuthoredUtc">When it was authored, in UTC.</param>
/// <param name="Subject">The first line of the message.</param>
/// <param name="CardId">The card a Pyxie merge commit landed, from its <c>Pyxie-Card</c> trailer; <see langword="null"/> for every other commit.</param>
public sealed record GitCommit(string Sha, string ShortSha, IReadOnlyList<string> ParentShas, string AuthorName, string AuthorEmail, DateTime AuthoredUtc, string Subject, int? CardId);
