using Pyxie.Runtime.Git;
using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>Bundles carry commits between the hub and a sandbox without either repository running anything of the other's: <see cref="GitClient.CreateBundleAsync"/> writes one, <see cref="GitClient.VerifyBundleAsync"/> checks the receiver has every commit it builds on, and <see cref="GitClient.FetchBundleAsync"/> takes one branch of it into a ref the caller names, checking every object it brings. The receiver is bare wherever it stands in for the hub.</summary>
public sealed class GitClientBundleTests
{
	private const string CardBranch = "pyxie/card-7";
	private const string Incoming = "refs/pyxie/incoming/card-7";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task CreateBundleAsync_OfWholeBranches_CarriesThemIntoARepositoryThatHasNothing()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(source, CardBranch);
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		await using BareRepository sandbox = await BareRepository.CreateEmptyAsync();
		string bundle = scratch.Combine("in", "hand-in.bundle");

		await hub.Repository.Git.CreateBundleAsync(hub.Folder, bundle, ["refs/heads/main", $"refs/heads/{CardBranch}"], Token);

		string[] heads = [$"{divergence.MainTip} refs/heads/main", $"{divergence.FeatureTip} refs/heads/{CardBranch}"];
		Assert.Equal(heads.Order(StringComparer.Ordinal), (await hub.Repository.RunGitAsync("bundle", "list-heads", bundle)).Split('\n').Order(StringComparer.Ordinal));
		await sandbox.Repository.Git.VerifyBundleAsync(sandbox.Folder, bundle, Token);
		await sandbox.Repository.Git.FetchBundleAsync(sandbox.Folder, bundle, $"+refs/heads/{CardBranch}:{Incoming}", Token);
		Assert.Equal(divergence.FeatureTip, await sandbox.Repository.GetHashAsync(Incoming));
		Assert.Equal(divergence.BaseTip, await sandbox.Repository.GetHashAsync($"{Incoming}~1"));
		Assert.Equal("from the feature branch", await sandbox.Repository.ReadFileAtAsync(Incoming, "feature-only.txt"));
		Assert.Equal(TempGitRepository.InitialContent, await sandbox.Repository.ReadFileAtAsync(Incoming, TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task FetchBundleAsync_OfABundleThatBuildsOnACommitTheReceiverHas_TakesTheNewCommitsIntoTheNamedRefOnly()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository sandbox = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(sandbox);
		string baseCommit = await sandbox.GetHashAsync();
		await sandbox.RunGitAsync("checkout", "--quiet", "-b", CardBranch);
		await sandbox.CommitFileAsync("work.txt", "the worker's first change");
		string tip = await sandbox.CommitFileAsync("more.txt", "the worker's second change");
		string bundle = scratch.Combine("out", "hand-out.bundle");

		await sandbox.Git.CreateBundleAsync(sandbox.Folder, bundle, [$"refs/heads/{CardBranch}", $"^{baseCommit}"], Token);
		await hub.Repository.Git.VerifyBundleAsync(hub.Folder, bundle, Token);
		await hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/{CardBranch}:{Incoming}", Token);

		Assert.Equal(tip, await hub.Repository.GetHashAsync(Incoming));
		Assert.True(await hub.Repository.Git.IsAncestorAsync(hub.Folder, baseCommit, tip, Token), "The commits that came back build on the commit the hub handed in.");
		Assert.Equal("the worker's first change", await hub.Repository.ReadFileAtAsync(Incoming, "work.txt"));
		Assert.Equal("the worker's second change", await hub.Repository.ReadFileAtAsync(Incoming, "more.txt"));
		Assert.False(await hub.Repository.Git.BranchExistsAsync(hub.Folder, CardBranch, Token), "Fetching into Pyxie's own ref must not create the branch the bundle names.");
		Assert.Equal(baseCommit, await hub.Repository.GetHashAsync("main"));
	}

	[Fact]
	public async Task FetchBundleAsync_WithAnExplicitRefspec_TakesOnlyThatRefAndNothingElseTheBundleNames()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		await hub.Repository.CreateBranchAsync("feature", "main");
		await source.RunGitAsync("checkout", "--quiet", "-b", "feature");
		string featureTip = await source.CommitFileAsync("feature.txt", "feature work");
		await source.RunGitAsync("tag", "--annotate", "v1", "--message", "A release on the fetched history");
		await source.CheckoutAsync("main");
		await source.CommitFileAsync("main.txt", "main moved on");
		await source.CreateBranchAsync("other");
		string bundle = scratch.Combine("everything.bundle");
		await source.Git.CreateBundleAsync(source.Folder, bundle, ["refs/heads/main", "refs/heads/feature", "refs/heads/other", "refs/tags/v1"], Token);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		await hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/feature:{Incoming}", Token);

		Assert.Equal(refsBefore.Append($"{Incoming} {featureTip}").Order(StringComparer.Ordinal), await hub.ListRefsAsync());
		Assert.False(File.Exists(Path.Combine(hub.Folder, "FETCH_HEAD")), "The fetch must not write FETCH_HEAD: the incoming ref is the only thing it leaves.");
	}

	[Fact]
	public async Task VerifyBundleAsync_WhenTheReceiverLacksACommitTheBundleBuildsOn_ThrowsAndFetchTakesNothing()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		string prerequisite = await source.CommitFileAsync("base.txt", "only the sender has this commit");
		await source.CommitFileAsync("work.txt", "built on it");
		string bundle = scratch.Combine("thin.bundle");
		await source.Git.CreateBundleAsync(source.Folder, bundle, ["refs/heads/main", $"^{prerequisite}"], Token);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => hub.Repository.Git.VerifyBundleAsync(hub.Folder, bundle, Token));
		await Assert.ThrowsAsync<GitException>(() => hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/main:{Incoming}", Token));

		Assert.Contains("git bundle failed", failure.Message, StringComparison.Ordinal);
		Assert.Equal(refsBefore, await hub.ListRefsAsync());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task VerifyBundleAsync_ForAFileThatIsNotABundle_ThrowsAGitException(bool fileExists)
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		string bundle = scratch.Combine("received.bundle");
		if (fileExists)
		{
			await File.WriteAllTextAsync(bundle, "This is not a bundle.", Token);
		}

		GitException failure = await Assert.ThrowsAsync<GitException>(() => hub.Repository.Git.VerifyBundleAsync(hub.Folder, bundle, Token));

		Assert.Contains("git bundle failed", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task FetchBundleAsync_ForAnObjectThatFailsGitsChecks_ThrowsAndTakesNothing()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository sandbox = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(sandbox);
		await using BareRepository control = await BareRepository.CloneAsync(sandbox);
		string baseCommit = await sandbox.GetHashAsync();
		string malformed = await WriteCommitWithABadTimeZoneAsync(sandbox, scratch);
		await sandbox.RunGitAsync("update-ref", $"refs/heads/{CardBranch}", malformed);
		string bundle = scratch.Combine("hand-out.bundle");
		await sandbox.Git.CreateBundleAsync(sandbox.Folder, bundle, [$"refs/heads/{CardBranch}", $"^{baseCommit}"], Token);
		Assert.False(await HasObjectAsync(hub, malformed), "The hub must not hold the malformed commit before the fetch, or git would skip unpacking the bundle.");
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		await hub.Repository.Git.VerifyBundleAsync(hub.Folder, bundle, Token);
		GitException failure = await Assert.ThrowsAsync<GitException>(() => hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/{CardBranch}:{Incoming}", Token));

		Assert.Contains("git fetch failed", failure.Message, StringComparison.Ordinal);
		Assert.False(await HasObjectAsync(hub, malformed), "A refused fetch must not leave the malformed commit in the hub.");
		Assert.Equal(refsBefore, await hub.ListRefsAsync());

		await control.Repository.RunGitAsync("-c", "fetch.fsckObjects=false", "-c", "transfer.fsckObjects=false", "fetch", "--quiet", "--no-write-fetch-head", bundle, $"+refs/heads/{CardBranch}:{Incoming}");
		Assert.Equal(malformed, await control.Repository.GetHashAsync(Incoming));
	}

	[Fact]
	public async Task FetchBundleAsync_ForABranchTheBundleDoesNotCarry_ThrowsAndWritesNothing()
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository sandbox = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(sandbox);
		string baseCommit = await sandbox.GetHashAsync();
		await sandbox.CommitFileAsync("work.txt", "on main, not on the card's branch");
		string bundle = scratch.Combine("hand-out.bundle");
		await sandbox.Git.CreateBundleAsync(sandbox.Folder, bundle, ["refs/heads/main", $"^{baseCommit}"], Token);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => hub.Repository.Git.FetchBundleAsync(hub.Folder, bundle, $"+refs/heads/{CardBranch}:{Incoming}", Token));

		Assert.Contains("git fetch failed", failure.Message, StringComparison.Ordinal);
		Assert.Equal(refsBefore, await hub.ListRefsAsync());
	}

	[Theory]
	[InlineData("refs/heads/no-such-branch")]
	[InlineData("refs/heads/main ^main")]
	public async Task CreateBundleAsync_WhenGitCannotWriteTheBundle_ThrowsAGitExceptionAndLeavesNoFile(string revisions)
	{
		await using ScratchFolder scratch = ScratchFolder.Create("pyxie-bundle");
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string bundle = scratch.Combine("refused.bundle");

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.CreateBundleAsync(repository.Folder, bundle, revisions.Split(' '), Token));

		Assert.Contains("git bundle failed", failure.Message, StringComparison.Ordinal);
		Assert.False(File.Exists(bundle), "A bundle git refused to write must not be left half-written.");
	}

	// The author line's time zone has five digits, which git's object checks reject (badTimezone) while everything else about the commit is sound.
	private static async Task<string> WriteCommitWithABadTimeZoneAsync(TempGitRepository repository, ScratchFolder scratch)
	{
		string tree = await repository.GetHashAsync("main^{tree}");
		string parent = await repository.GetHashAsync("main");
		string objectFile = scratch.Combine("malformed-commit.txt");
		string identity = $"{TempGitRepository.AuthorName} <tests@pyxie.invalid> 1700000000";
		await File.WriteAllTextAsync(objectFile, $"tree {tree}\nparent {parent}\nauthor {identity} +99999\ncommitter {identity} +0000\n\nA commit git's checks refuse\n", Token);

		return await repository.RunGitAsync("hash-object", "-t", "commit", "--literally", "-w", objectFile);
	}

	private static async Task<bool> HasObjectAsync(BareRepository repository, string sha)
	{
		ProcessResult result = await repository.Repository.TryRunGitAsync("cat-file", "-e", sha);

		return result.Succeeded;
	}
}
