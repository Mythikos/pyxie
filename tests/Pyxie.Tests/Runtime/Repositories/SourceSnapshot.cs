using Pyxie.Runtime.Processes;
using Pyxie.Tests.Runtime.Integration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>Everything about a repository folder that an import or a pipeline run must never change: what <see cref="CheckoutSnapshot"/> captures (the bytes of every working file, the commit checked out, the index, git's status) plus the branch checked out, every ref, the worktrees, and the repository's own <c>config</c> and <c>info/exclude</c>, which in-place use of a folder used to rewrite. Capturing it before and after proves a folder was only ever read.</summary>
/// <param name="Checkout">The working tree, the index, and the status.</param>
/// <param name="Branch">The branch HEAD names, or <c>HEAD</c> when it is detached.</param>
/// <param name="Refs">Every ref with the object it points at (<c>git for-each-ref</c>).</param>
/// <param name="Worktrees">The worktrees git lists for the repository.</param>
/// <param name="Config">The repository's <c>.git/config</c>.</param>
/// <param name="Exclude">The repository's <c>.git/info/exclude</c>, or nothing when it has none.</param>
public sealed record SourceSnapshot(CheckoutSnapshot Checkout, string Branch, string Refs, string Worktrees, string Config, string Exclude)
{
	/// <summary>Captures a repository folder.</summary>
	/// <param name="source">The repository whose own folder is captured.</param>
	/// <returns>The snapshot.</returns>
	public static async Task<SourceSnapshot> CaptureAsync(TempGitRepository source)
	{
		string gitFolder = Path.Combine(source.Folder, ".git");
		string exclude = Path.Combine(gitFolder, "info", "exclude");
		ProcessResult branch = await source.TryRunGitAsync("rev-parse", "--abbrev-ref", "HEAD");

		return new SourceSnapshot(await CheckoutSnapshot.CaptureAsync(source), branch.Output.Trim(), await source.RunGitAsync("for-each-ref", "--format=%(refname) %(objectname)"), await source.RunGitAsync("worktree", "list", "--porcelain"), await File.ReadAllTextAsync(Path.Combine(gitFolder, "config"), TestContext.Current.CancellationToken), File.Exists(exclude) ? await File.ReadAllTextAsync(exclude, TestContext.Current.CancellationToken) : string.Empty);
	}

	/// <summary>Fails the test unless a repository folder is exactly as this snapshot captured it.</summary>
	/// <param name="source">The repository whose own folder is compared.</param>
	/// <returns>A task that completes when the comparison passed.</returns>
	public async Task AssertUnchangedAsync(TempGitRepository source)
	{
		SourceSnapshot after = await CaptureAsync(source);

		Assert.Equal(Checkout.Files, after.Checkout.Files);
		Assert.Equal(Checkout.Head, after.Checkout.Head);
		Assert.Equal(Checkout.Index, after.Checkout.Index);
		Assert.Equal(Checkout.Status, after.Checkout.Status);
		Assert.Equal(Branch, after.Branch);
		Assert.Equal(Refs, after.Refs);
		Assert.Equal(Worktrees, after.Worktrees);
		Assert.Equal(Config, after.Config);
		Assert.Equal(Exclude, after.Exclude);
	}
}
