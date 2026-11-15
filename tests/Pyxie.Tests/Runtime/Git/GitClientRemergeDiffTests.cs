using System.Text;

using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary><see cref="GitClient.RemergeDiffAsync"/> shows how a merge commit resolved its conflicts, which is what a reviewer of a merge agent's resolution looks at: git merges the commit's parents again, with conflict markers, and diffs that against the commit. It runs in the hub, which is bare, and leaves no ref and no object directory behind.</summary>
public sealed class GitClientRemergeDiffTests
{
	private const int NoLimit = 1_000_000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	// Merges the feature branch into main, which conflicts in the files, and commits the merge with the content given for each file.
	private static async Task<string> ResolveAsync(TempGitRepository repository, string branch, params (string Path, string Content)[] resolved)
	{
		Assert.False((await repository.TryRunGitAsync("merge", "--no-ff", "--no-edit", branch)).Succeeded, "The merge was meant to conflict.");
		foreach ((string path, string content) in resolved)
		{
			await repository.WriteFileAsync(path, content);
		}

		await repository.RunGitAsync("add", "--all");
		await repository.RunGitAsync("commit", "--quiet", "--no-edit");

		return await repository.GetHashAsync();
	}

	[Fact]
	public async Task RemergeDiffAsync_ForAMergeThatResolvedAConflict_ShowsTheMarkersGitWroteGoingAndTheResolutionComing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await Divergence.ConflictingAsync(source, "feature", "first.txt");
		string merge = await ResolveAsync(source, "feature", ("first.txt", "resolved by hand"));
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		string diff = await hub.Repository.Git.RemergeDiffAsync(hub.Folder, merge, NoLimit, Token);

		Assert.Contains("first.txt", diff, StringComparison.Ordinal);
		Assert.Contains("-<<<<<<<", diff, StringComparison.Ordinal);
		Assert.Contains($"-{Divergence.MainContent("first.txt")}", diff, StringComparison.Ordinal);
		Assert.Contains($"-{Divergence.FeatureContent("first.txt")}", diff, StringComparison.Ordinal);
		Assert.Contains("+resolved by hand", diff, StringComparison.Ordinal);
		Assert.Equal(refsBefore, await hub.ListRefsAsync());
		Assert.Empty(Directory.EnumerateDirectories(Path.Combine(hub.Folder, "objects"), "tmp_objdir*"));
	}

	[Fact]
	public async Task RemergeDiffAsync_ForAMergeThatWasClean_ShowsNothing()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await Divergence.CleanAsync(repository, "feature");
		await repository.RunGitAsync("merge", "--no-ff", "--no-edit", "feature");

		string diff = await repository.Git.RemergeDiffAsync(repository.Folder, "HEAD", NoLimit, Token);

		Assert.Equal(string.Empty, diff);
	}

	[Fact]
	public async Task RemergeDiffAsync_ForACommitThatIsNotAMerge_ShowsItsOwnChanges()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string commit = await repository.CommitFileAsync("plain.txt", "not a merge");

		string diff = await repository.Git.RemergeDiffAsync(repository.Folder, commit, NoLimit, Token);

		Assert.Contains("+not a merge", diff, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RemergeDiffAsync_ForADiffLongerThanTheLimit_KeepsItsBeginningAndEndWithAMarker()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		StringBuilder baseText = new();
		StringBuilder mainText = new();
		StringBuilder featureText = new();
		StringBuilder resolved = new();
		for (int line = 1; line <= 300; line++)
		{
			baseText.Append($"line {line} of the base\n");
			mainText.Append($"line {line} of main\n");
			featureText.Append($"line {line} of the feature\n");
			resolved.Append($"line {line} resolved\n");
		}

		await repository.CommitFileAsync("big.txt", baseText.ToString());
		await repository.CreateBranchAsync("feature");
		await repository.CheckoutAsync("feature");
		await repository.CommitFileAsync("big.txt", featureText.ToString());
		await repository.CheckoutAsync("main");
		await repository.CommitFileAsync("big.txt", mainText.ToString());
		string merge = await ResolveAsync(repository, "feature", ("big.txt", resolved.ToString()));

		string diff = await repository.Git.RemergeDiffAsync(repository.Folder, merge, 3000, Token);

		Assert.Contains("[truncated: ", diff, StringComparison.Ordinal);
		Assert.True(diff.Length < 3200, $"The diff was {diff.Length} characters long.");
		Assert.Contains("diff", diff[..200], StringComparison.Ordinal);
		Assert.Contains("line 300 resolved", diff, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RemergeDiffAsync_ForACommitThatDoesNotExist_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.RemergeDiffAsync(repository.Folder, new string('0', 40), NoLimit, Token));

		Assert.StartsWith("git show failed", failure.Message, StringComparison.Ordinal);
	}
}
