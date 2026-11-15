using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary><see cref="GitClient.DeleteRefAsync"/> removes the ref a take-back fetched into, whether or not it is there, and <see cref="GitClient.DeleteBranchAsync"/> removes a card's branch from the hub whatever it holds. Neither touches any other ref.</summary>
public sealed class GitClientRefDeletionTests
{
	private const string Incoming = "refs/pyxie/incoming/card-1";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task DeleteRefAsync_RemovesTheRefAndLeavesEveryOtherRefAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("pyxie/card-1");
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		string head = await hub.Repository.GetHashAsync("main");
		await hub.Repository.RunGitAsync("update-ref", Incoming, head);
		await hub.Repository.RunGitAsync("update-ref", "refs/pyxie/incoming/card-2", head);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		await hub.Repository.Git.DeleteRefAsync(hub.Folder, Incoming, Token);

		Assert.Equal(refsBefore.Where(line => !line.StartsWith($"{Incoming} ", StringComparison.Ordinal)), await hub.ListRefsAsync());
	}

	[Fact]
	public async Task DeleteRefAsync_ForARefThatIsNotThere_DoesNothing()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		IReadOnlyList<string> refsBefore = await hub.ListRefsAsync();

		await hub.Repository.Git.DeleteRefAsync(hub.Folder, Incoming, Token);

		Assert.Equal(refsBefore, await hub.ListRefsAsync());
	}

	[Fact]
	public async Task DeleteRefAsync_ForARefGitPacked_RemovesIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using BareRepository hub = await BareRepository.CloneAsync(source);
		await hub.Repository.RunGitAsync("update-ref", Incoming, "main");
		await hub.Repository.RunGitAsync("pack-refs", "--all");
		Assert.False(File.Exists(Path.Combine(hub.Folder, "refs", "pyxie", "incoming", "card-1")), "The setup must leave the ref only in packed-refs.");

		await hub.Repository.Git.DeleteRefAsync(hub.Folder, Incoming, Token);

		Assert.False((await hub.Repository.TryRunGitAsync("show-ref", "--verify", "--quiet", Incoming)).Succeeded, "The packed ref must be gone.");
		Assert.True(await hub.Repository.Git.BranchExistsAsync(hub.Folder, "main", Token), "The other packed refs must stay.");
	}

	[Fact]
	public async Task DeleteBranchAsync_InTheHub_DeletesABranchWithUnmergedCommits()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("pyxie/card-1");
		await source.CheckoutAsync("pyxie/card-1");
		await source.CommitFileAsync("only-here.txt", "not on main");
		await source.CheckoutAsync("main");
		await using BareRepository hub = await BareRepository.CloneAsync(source);

		await hub.Repository.Git.DeleteBranchAsync(hub.Folder, "pyxie/card-1", Token);

		Assert.False(await hub.Repository.Git.BranchExistsAsync(hub.Folder, "pyxie/card-1", Token), "The branch must be deleted even though its commit is not on main.");
		Assert.Equal(await source.GetHashAsync("main"), await hub.Repository.GetHashAsync("main"));
	}

	[Fact]
	public async Task DeleteBranchAsync_ForTheCheckedOutBranch_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.DeleteBranchAsync(repository.Folder, "main", Token));

		Assert.Contains("git branch failed", failure.Message, StringComparison.Ordinal);
		Assert.True(await repository.Git.BranchExistsAsync(repository.Folder, "main", Token), "The checked-out branch must survive.");
	}

	[Fact]
	public async Task DeleteBranchAsync_ForABranchThatDoesNotExist_ThrowsAGitException()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		GitException failure = await Assert.ThrowsAsync<GitException>(() => repository.Git.DeleteBranchAsync(repository.Folder, "no-such-branch", Token));

		Assert.Contains("git branch failed", failure.Message, StringComparison.Ordinal);
	}
}
