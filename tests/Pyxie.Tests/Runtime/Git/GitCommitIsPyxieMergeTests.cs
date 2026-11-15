using System.Globalization;

using Pyxie.Runtime.Git;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A commit is one of Pyxie's merges when it has more than one parent and Pyxie's identity authored it. The trailer that names a card plays no part, because Pyxie's merge of the target branch into a card's branch has none, and a person can write one in any message. A single-parent commit signed Pyxie is an agent's work, or what Pyxie committed for an agent that left it uncommitted.</summary>
public sealed class GitCommitIsPyxieMergeTests
{
	private const string PersonName = "Ada Lovelace";
	private const string PersonEmail = "ada@example.test";

	[Theory]
	[InlineData("A merge of two parents by Pyxie", 2, PyxieIdentity.Name, PyxieIdentity.Email, null, true)]
	[InlineData("An octopus merge of three parents by Pyxie", 3, PyxieIdentity.Name, PyxieIdentity.Email, null, true)]
	[InlineData("A merge by Pyxie whose trailer names its card", 2, PyxieIdentity.Name, PyxieIdentity.Email, 7, true)]
	[InlineData("A single-parent commit by Pyxie, an agent's work", 1, PyxieIdentity.Name, PyxieIdentity.Email, null, false)]
	[InlineData("A single-parent commit by Pyxie whose trailer names a card", 1, PyxieIdentity.Name, PyxieIdentity.Email, 7, false)]
	[InlineData("A root commit by Pyxie", 0, PyxieIdentity.Name, PyxieIdentity.Email, null, false)]
	[InlineData("A merge of two parents by a person", 2, PersonName, PersonEmail, null, false)]
	[InlineData("A merge by a person whose trailer names a card", 2, PersonName, PersonEmail, 48, false)]
	[InlineData("A single-parent commit by a person whose trailer names a card", 1, PersonName, PersonEmail, 48, false)]
	[InlineData("A merge under Pyxie's name and another email address", 2, PyxieIdentity.Name, PersonEmail, null, false)]
	[InlineData("A merge under another name and Pyxie's email address", 2, PersonName, PyxieIdentity.Email, null, false)]
	[InlineData("A merge under Pyxie's name in other case", 2, "pyxie", PyxieIdentity.Email, null, false)]
	[InlineData("A merge under Pyxie's email address in other case", 2, PyxieIdentity.Name, "PYXIE@LOCALHOST", null, false)]
	public void IsPyxieMerge_IsTrueOnlyForACommitOfSeveralParentsUnderPyxiesExactIdentity(string description, int parentCount, string authorName, string authorEmail, int? cardId, bool expected)
	{
		GitCommit commit = new(Sha(0), Sha(0)[..7], [.. Enumerable.Range(1, parentCount).Select(Sha)], authorName, authorEmail, new DateTime(2029, 12, 31, 22, 0, 0, DateTimeKind.Utc), "Subject", cardId);

		Assert.True(commit.IsPyxieMerge == expected, $"{description}: IsPyxieMerge should be {expected}.");
	}

	private static string Sha(int index)
	{
		return index.ToString("x40", CultureInfo.InvariantCulture);
	}
}
