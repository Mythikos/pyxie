using System.Collections.Frozen;

namespace Pyxie.Runtime.Git;

/// <summary>The identity Pyxie writes commits under: the merge commits it makes in the hub, and the commits it makes in a card's sandbox for an agent that left its work uncommitted. It owns the name and the address, so a commit of Pyxie's is recognizable and never carries a person's or a machine's git identity.</summary>
public static class PyxieIdentity
{
	/// <summary>The name Pyxie commits under.</summary>
	public const string Name = "Pyxie";

	/// <summary>The email address Pyxie commits under.</summary>
	public const string Email = "pyxie@localhost";

	/// <summary>Gets the environment that makes a git command author and commit as Pyxie. git reads these variables, and <c>author.*</c> and <c>committer.*</c> configuration, before <c>user.*</c>, so <c>-c user.name</c> and <c>-c user.email</c> would lose to an identity a repository, an image, or a worker configured; the variables win over all of them.</summary>
	public static IReadOnlyDictionary<string, string> Environment { get; } = new Dictionary<string, string>
	{
		["GIT_AUTHOR_NAME"] = Name,
		["GIT_AUTHOR_EMAIL"] = Email,
		["GIT_COMMITTER_NAME"] = Name,
		["GIT_COMMITTER_EMAIL"] = Email,
	}.ToFrozenDictionary();
}
