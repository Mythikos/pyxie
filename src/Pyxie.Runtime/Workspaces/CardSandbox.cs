using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Workspaces;

/// <summary>A round's sandbox with the card's repository in it, checked out where the round starts. Disposing it disposes the sandbox.</summary>
public sealed class CardSandbox : IAsyncDisposable
{
	/// <summary>Initializes a new instance of the <see cref="CardSandbox"/> class.</summary>
	/// <param name="sandbox">The running sandbox.</param>
	/// <param name="repositoryPath">The card's repository, as the sandbox sees it.</param>
	/// <param name="branch">The card's branch.</param>
	/// <param name="baseCommit">The commit the round starts from: the card branch's tip in the hub, or the target branch's for a card whose branch does not exist yet.</param>
	/// <param name="targetCommit">The target branch's tip in the hub, read when the sandbox was opened. The hand-in bundle's target branch can be ahead of it when the branch moved while the bundle was made, so a merge is made of this commit and nothing else.</param>
	/// <param name="branchIsNew">Whether the card's branch is not in the hub yet.</param>
	/// <param name="folder">The host folder of this sandbox: its input, output, and sign-in.</param>
	public CardSandbox(ISandbox sandbox, string repositoryPath, string branch, string baseCommit, string targetCommit, bool branchIsNew, string folder)
	{
		Sandbox = sandbox;
		RepositoryPath = repositoryPath;
		Branch = branch;
		BaseCommit = baseCommit;
		TargetCommit = targetCommit;
		BranchIsNew = branchIsNew;
		Folder = folder;
	}

	/// <summary>Gets the running sandbox.</summary>
	public ISandbox Sandbox { get; }

	/// <summary>Gets the card's repository, as the sandbox sees it.</summary>
	public string RepositoryPath { get; }

	/// <summary>Gets the card's branch.</summary>
	public string Branch { get; }

	/// <summary>Gets the commit the round starts from.</summary>
	public string BaseCommit { get; }

	/// <summary>Gets the target branch's tip in the hub, as read when the sandbox was opened.</summary>
	public string TargetCommit { get; }

	/// <summary>Gets a value indicating whether the card's branch is not in the hub yet.</summary>
	public bool BranchIsNew { get; }

	/// <summary>Gets the host folder of this sandbox.</summary>
	public string Folder { get; }

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return Sandbox.DisposeAsync();
	}
}
