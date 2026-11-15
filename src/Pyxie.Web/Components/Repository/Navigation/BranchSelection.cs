using Pyxie.Runtime.Git;

namespace Pyxie.Web.Components.Repository.Navigation;

/// <summary>Decides which branches the Files and History tabs offer, in which order, and which one is selected. Only a branch that exists is ever selected, so an address cannot send an arbitrary revision to git.</summary>
public static class BranchSelection
{
	/// <summary>Orders branch names for the selector: the target branch first, the others in the order given.</summary>
	/// <param name="branches">The local branches.</param>
	/// <param name="targetBranch">The branch finished cards are merged into.</param>
	/// <returns>The branch names.</returns>
	public static IReadOnlyList<string> Order(IReadOnlyList<GitBranch> branches, string targetBranch)
	{
		return [.. branches.Select(branch => branch.Name).OrderBy(name => string.Equals(name, targetBranch, StringComparison.Ordinal) ? 0 : 1)];
	}

	/// <summary>Picks the branch to show.</summary>
	/// <param name="requested">The branch the address names; empty when it names none.</param>
	/// <param name="ordered">The branch names in selector order, from <see cref="Order"/>.</param>
	/// <returns>The requested branch when it exists, otherwise the first one (the target branch while it exists); empty when there are no branches.</returns>
	public static string Resolve(string requested, IReadOnlyList<string> ordered)
	{
		if (requested.Length > 0 && ordered.Contains(requested, StringComparer.Ordinal))
		{
			return requested;
		}

		return ordered.Count > 0 ? ordered[0] : string.Empty;
	}

	/// <summary>Gets the revision that names a branch to git without ambiguity: a tag with the same name would otherwise win.</summary>
	/// <param name="branch">The branch name.</param>
	/// <returns>The full reference name.</returns>
	public static string Revision(string branch)
	{
		return $"refs/heads/{branch}";
	}
}
