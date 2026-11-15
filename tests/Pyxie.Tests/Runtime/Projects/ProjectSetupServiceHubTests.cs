using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Processes;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Pyxie's clone is a bare hub: it has no files checked out, because cards work in their own sandboxes and merges are written straight into the target branch. People clone it, pull from it, and push to it, and the hub keeps no remote for the source. People's own branches are theirs to rewrite and delete; the hub's update hook keeps the target branch from being rewritten or deleted, because cards are built on it, and keeps pushes off the pipeline's <c>pyxie/</c> branches. git refuses some of those pushes by itself (deleting the branch HEAD names), so every refusal here is checked by the hook's own message.</summary>
public sealed class ProjectSetupServiceHubTests
{
	private const string RewriteRefused = "main cannot be rewritten: cards are built on it.";
	private const string DeleteRefused = "main cannot be deleted: cards are built on it.";
	private const string PipelineBranchRefused = "belongs to Pyxie's pipeline";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(RepositorySource.Folder)]
	[InlineData(RepositorySource.Url)]
	public async Task Import_MakesABareHubWhoseHeadIsTheTargetBranchWithNoRemoteAndWhichRunsTheHubsOwnUpdateHook(RepositorySource source)
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await origin.RunGitAsync("checkout", "--quiet", "-b", "develop");
		await origin.CommitFileAsync("docs/develop-only.txt", "only on develop");
		await using RepositoryRig rig = new();

		Repository connected = ResultAssert.Succeeded(source == RepositorySource.Folder ? await rig.Setup.ImportFolderAsync(origin.Folder, Token) : await rig.Setup.CloneAsync(origin.Folder, Token));

		Assert.Equal(source, connected.Source);
		Assert.Equal("true", await origin.RunGitAsync("-C", connected.Path, "rev-parse", "--is-bare-repository"));
		Assert.False(Directory.Exists(Path.Combine(connected.Path, ".git")), "A bare repository keeps git's files in its folder itself.");
		Assert.Equal("develop", connected.TargetBranch);
		Assert.Equal("refs/heads/develop", await origin.RunGitAsync("-C", connected.Path, "symbolic-ref", "HEAD"));
		Assert.Empty(await origin.RunGitAsync("-C", connected.Path, "remote"));
		Assert.Empty(await origin.RunGitAsync("-C", connected.Path, "for-each-ref", "refs/remotes"));
		Assert.False((await origin.TryRunGitAsync("-C", connected.Path, "rev-parse", "--abbrev-ref", "develop@{upstream}")).Succeeded, "The target branch has no upstream: nothing in the hub stands for the source.");
		Assert.Equal(await origin.GetHashAsync("develop"), await rig.Git.RevParseAsync(connected.Path, "HEAD", Token));
		Assert.Equal("only on develop", await rig.ReadFileAtAsync(connected.Path, "HEAD", "docs/develop-only.txt"));

		Assert.Equal("develop", await origin.RunGitAsync("-C", connected.Path, "config", "pyxie.targetBranch"));
		string hooks = await origin.RunGitAsync("-C", connected.Path, "config", "core.hooksPath");
		Assert.True(Path.IsPathFullyQualified(hooks), "The hub names its own hooks folder in full, so a hooks folder in someone's global git config cannot replace it.");
		string hook = await File.ReadAllTextAsync(Path.Combine(hooks, "update"), Token);
		Assert.StartsWith("#!/bin/sh\n", hook, StringComparison.Ordinal);
		Assert.DoesNotContain('\r', hook);
		Assert.True(File.Exists(Path.Combine(connected.Path, "hooks", "update")), "The hook lives in the hub's own hooks folder.");
		Assert.False((await origin.TryRunGitAsync("-C", connected.Path, "config", "--get", "remote.origin.url")).Succeeded, "The hub keeps no URL for the source.");
		Assert.NotEqual("true", (await origin.TryRunGitAsync("-C", connected.Path, "config", "receive.denyNonFastForwards")).Output.Trim());
		Assert.NotEqual("true", (await origin.TryRunGitAsync("-C", connected.Path, "config", "receive.denyDeletes")).Output.Trim());
	}

	[Fact]
	public async Task Hub_TakesAPushToTheTargetBranchAndRefusesToRewriteIt()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		await using TempGitRepository hub = await TempGitRepository.AttachAsync(connected.Path);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(hub);
		string pushed = await person.CommitFileAsync("pushed.txt", "from a person");

		await person.PushAsync("main");

		Assert.Equal(pushed, await hub.GetHashAsync("main"));
		Assert.Equal("from a person", await hub.ReadFileAtAsync("main", "pushed.txt"));

		await person.RunGitAsync("reset", "--quiet", "--hard", "HEAD~1");
		await person.CommitFileAsync("rewritten.txt", "a different history");
		ProcessResult rewritten = await person.TryPushAsync("--force", "main");

		Assert.False(rewritten.Succeeded, "A forced push that rewrites the target branch must be refused.");
		Assert.Contains(RewriteRefused, rewritten.Output, StringComparison.Ordinal);
		Assert.Equal(pushed, await hub.GetHashAsync("main"));
	}

	[Fact]
	public async Task Hub_RefusesToDeleteTheTargetBranchEvenWhenHeadNamesAnotherBranch()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		await using TempGitRepository hub = await TempGitRepository.AttachAsync(connected.Path);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(hub);
		string main = await hub.GetHashAsync("main");

		ProcessResult whileHead = await person.TryPushAsync("--delete", "main");

		Assert.False(whileHead.Succeeded, "Deleting the target branch by push must be refused.");
		Assert.Equal(main, await hub.GetHashAsync("main"));

		// git refuses to delete the branch HEAD names by itself; with HEAD moved away, only the hook stands between a push and the target branch.
		await person.PushAsync("main:spare");
		await hub.RunGitAsync("symbolic-ref", "HEAD", "refs/heads/spare");
		ProcessResult elsewhere = await person.TryPushAsync("--delete", "main");

		Assert.False(elsewhere.Succeeded, "Deleting the target branch by push must be refused whatever HEAD names.");
		Assert.Contains(DeleteRefused, elsewhere.Output, StringComparison.Ordinal);
		Assert.Equal(main, await hub.GetHashAsync("main"));
	}

	[Fact]
	public async Task Hub_LeavesAPersonsOwnBranchesToThemToCreateRewriteAndDelete()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		await using TempGitRepository hub = await TempGitRepository.AttachAsync(connected.Path);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(hub);
		await person.RunGitAsync("checkout", "--quiet", "-b", "mine");
		await person.CommitFileAsync("draft.txt", "a first try");

		await person.PushAsync("mine");
		await person.RunGitAsync("commit", "--quiet", "--amend", "-m", "A second try");
		string rewritten = await person.RunGitAsync("rev-parse", "HEAD");
		await person.PushAsync("--force", "mine");

		Assert.Equal(rewritten, await hub.GetHashAsync("mine"));

		await person.PushAsync("--delete", "mine");

		Assert.False((await hub.TryRunGitAsync("rev-parse", "--verify", "--quiet", "refs/heads/mine")).Succeeded, "A person deletes their own branch by push.");
	}

	[Fact]
	public async Task Hub_RefusesEveryPushToAPipelineBranch()
	{
		await using TempGitRepository origin = await TempGitRepository.CreateAsync();
		await using RepositoryRig rig = new();
		Repository connected = ResultAssert.Succeeded(await rig.Setup.ImportFolderAsync(origin.Folder, Token));
		await using TempGitRepository hub = await TempGitRepository.AttachAsync(connected.Path);
		string main = await hub.GetHashAsync("main");

		// No worktree has the branch checked out, so git itself would take the push; only the hook refuses it.
		await hub.RunGitAsync("update-ref", "refs/heads/pyxie/card-7", main);
		await using ClonedRepository person = await ClonedRepository.CreateAsync(hub);
		string commit = await person.CommitFileAsync("sneaky.txt", "not the pipeline's work");

		ProcessResult updated = await person.TryPushAsync("HEAD:pyxie/card-7");
		ProcessResult deleted = await person.TryPushAsync("--delete", "pyxie/card-7");
		ProcessResult created = await person.TryPushAsync("HEAD:pyxie/card-8");

		Assert.False(updated.Succeeded, "Moving a pipeline branch by push must be refused.");
		Assert.Contains(PipelineBranchRefused, updated.Output, StringComparison.Ordinal);
		Assert.False(deleted.Succeeded, "Deleting a pipeline branch by push must be refused.");
		Assert.Contains(PipelineBranchRefused, deleted.Output, StringComparison.Ordinal);
		Assert.False(created.Succeeded, "Creating a branch under pyxie/ by push must be refused.");
		Assert.Contains(PipelineBranchRefused, created.Output, StringComparison.Ordinal);
		Assert.Equal(main, await hub.GetHashAsync("pyxie/card-7"));
		Assert.False((await hub.TryRunGitAsync("rev-parse", "--verify", "--quiet", "refs/heads/pyxie/card-8")).Succeeded, "The refused branch was never created.");
		Assert.NotEqual(commit, await hub.GetHashAsync("main"));
	}
}
