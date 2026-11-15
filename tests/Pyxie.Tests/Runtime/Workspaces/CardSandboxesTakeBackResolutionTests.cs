using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>A merge agent's resolution comes back like a worker's work, with checks of its own made in the hub before the card's branch moves: the merge was completed, what came back is exactly one merge commit of the card's branch and the target branch commit the merge started from, and none of the files that conflicted has conflict markers left. Whatever is wrong, the hub is unchanged and the next resolve round starts over.</summary>
public sealed class CardSandboxesTakeBackResolutionTests
{
	private const string ReceivedFile = "received.bundle";

	private static readonly AgentSnapshot s_merger = new("Merger", HarnessKind.Other, "merger", "--resolve");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string Message(CardSandbox card)
	{
		return $"Merge main into {card.Branch}";
	}

	private static string Short(string commit)
	{
		return commit[..12];
	}

	private static async Task AssertNothingLeftAsync(CardSandboxRig rig, CardSandbox card)
	{
		Assert.Empty(await rig.PyxieRefsAsync());
		Assert.False(File.Exists(Path.Combine(card.Folder, ReceivedFile)), "The bundle copied out of the sandbox must be deleted.");
	}

	[Fact]
	public async Task TakeBackResolutionAsync_ForAResolvedMergeThatPyxieCommitted_MovesTheCardBranchToTheMergeCommit()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, string targetTip) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(card), TempGitRepository.InitialFile, "resolved with both");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Equal($"{cardTip} {targetTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", "pyxie/card-1"));
		Assert.Equal("Merge main into pyxie/card-1", await rig.Hub.RunGitAsync("log", "-1", "--format=%s", "pyxie/card-1"));
		Assert.Equal("resolved with both", await rig.Hub.ReadFileAtAsync("pyxie/card-1", TempGitRepository.InitialFile));
		Assert.Equal(await rig.HashInAsync(CardSandboxRig.HostPath(card)), await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheAgentCommittedTheMergeItself_MovesTheCardBranchToIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, string targetTip) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "resolved by the agent");
		await rig.RunGitInAsync(repository, "add", "--all");
		await rig.RunGitInAsync(repository, "commit", "--quiet", "--no-edit");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Equal($"{cardTip} {targetTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", "pyxie/card-1"));
		Assert.Equal("resolved by the agent", await rig.Hub.ReadFileAtAsync("pyxie/card-1", TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task TakeBackResolutionAsync_ForAMergeThatWasClean_MovesTheCardBranchToTheMergeCommit()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		string cardTip = await rig.WorkRoundAsync(1, 1, "work.txt", "the card's file");
		string targetTip = await rig.MoveTargetAsync("moved-on.txt", "the target's file");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Equal($"{cardTip} {targetTip}", await rig.Hub.RunGitAsync("log", "-1", "--format=%P", "pyxie/card-1"));
		Assert.Equal("the target's file", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "moved-on.txt"));
		Assert.Equal("the card's file", await rig.Hub.ReadFileAtAsync("pyxie/card-1", "work.txt"));
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheMergeWasAbandoned_RefusesItEvenThoughNothingChanged()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, _) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await rig.RunGitInAsync(CardSandboxRig.HostPath(card), "merge", "--abort");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal("The merge of main was not completed.", problem);
		Assert.Equal(cardTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheAgentAbandonedTheMergeAndCommittedAFileInstead_RefusesTheCommitBecauseItIsNotAMerge()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, _) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		string repository = CardSandboxRig.HostPath(card);
		await rig.RunGitInAsync(repository, "merge", "--abort");
		await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "written without merging");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));
		string tip = await rig.HashInAsync(repository);

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal($"The resolution must be one merge commit of pyxie/card-1 and main. What came back is {Short(tip)}, a commit with one parent, {Short(cardTip)}.", problem);
		Assert.Equal(cardTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheAgentMadeAnotherCommitAfterTheMerge_RefusesTheResolution()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, _) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		string repository = CardSandboxRig.HostPath(card);
		await CardSandboxRig.WriteAsync(repository, TempGitRepository.InitialFile, "resolved with both");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));
		string merge = await rig.HashInAsync(repository);
		string tip = await rig.CommitInAsync(repository, "extra.txt", "an unrelated change");

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal($"The resolution must be one merge commit of pyxie/card-1 and main. What came back is {Short(tip)}, a commit with one parent, {Short(merge)}.", problem);
		Assert.Equal(cardTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheMergeIsOfAnotherCommitThanTheTargetBranchOne_RefusesTheResolution()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, _) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		string repository = CardSandboxRig.HostPath(card);
		await rig.RunGitInAsync(repository, "merge", "--abort");
		await rig.RunGitInAsync(repository, "checkout", "--quiet", "-b", "side");
		string side = await rig.CommitInAsync(repository, "side.txt", "a commit of the agent's own");
		await rig.RunGitInAsync(repository, "checkout", "--quiet", "pyxie/card-1");
		await rig.RunGitInAsync(repository, "merge", "--quiet", "--no-ff", "--no-edit", "side");
		string tip = await rig.HashInAsync(repository);

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal($"The resolution must be one merge commit of pyxie/card-1 and main. What came back is {Short(tip)}, a commit with 2 parents, {Short(cardTip)}, {Short(side)}.", problem);
		Assert.Equal(cardTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheConflictMarkersAreLeft_RefusesTheResolutionNamingTheFile()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(string cardTip, _) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal($"Conflict markers are left in: {TempGitRepository.InitialFile}.", problem);
		Assert.Equal(cardTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenSeveralFilesConflict_NamesOnlyTheOnesWithMarkersLeftInOrder()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.WriteFileAsync("c.txt", "base c");
		await source.WriteFileAsync("a.txt", "base a");
		await source.WriteFileAsync("docs/b.txt", "base b");
		await source.CommitAsync("Add the files both sides change");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await using (CardSandbox work = await rig.OpenAsync(1, 1, RoundKind.Work))
		{
			string worker = CardSandboxRig.HostPath(work);
			await CardSandboxRig.WriteAsync(worker, "c.txt", "card c");
			await CardSandboxRig.WriteAsync(worker, "a.txt", "card a");
			await CardSandboxRig.WriteAsync(worker, "docs/b.txt", "card b");
			Assert.Null(await rig.Cards.CommitAsync(work, "The card's work", Token));
			Assert.Null(await rig.Cards.TakeBackAsync(rig.Context, work, Token));
		}

		await using (ClonedRepository person = await ClonedRepository.CreateAsync(rig.Hub))
		{
			await person.RunGitAsync("checkout", "--quiet", "-B", "main", "origin/main");
			await person.CommitFileAsync("c.txt", "target c");
			await person.CommitFileAsync("a.txt", "target a");
			await person.CommitFileAsync("docs/b.txt", "target b");
			await person.PushAsync("main");
		}

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(card), "c.txt", "resolved c");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal("Conflict markers are left in: a.txt, docs/b.txt.", problem);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenAMarkerLikeLineWasInTheFileBeforeTheMerge_AcceptsTheResolution()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CommitFileAsync(TempGitRepository.InitialFile, "Title\n=======\nthe body");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, TempGitRepository.InitialFile, "Title\n=======\nthe card's body");
		await rig.MoveTargetAsync(TempGitRepository.InitialFile, "Title\n=======\nthe target's body");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(card), TempGitRepository.InitialFile, "Title\n=======\nboth bodies");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Equal("Title\n=======\nboth bodies", await rig.Hub.ReadFileAtAsync("pyxie/card-1", TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheAgentDeletedAFileThatConflicted_AcceptsTheResolution()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, TempGitRepository.InitialFile, "the card's version");
		await using (ClonedRepository person = await ClonedRepository.CreateAsync(rig.Hub))
		{
			await person.RunGitAsync("checkout", "--quiet", "-B", "main", "origin/main");
			await person.RunGitAsync("rm", "--quiet", TempGitRepository.InitialFile);
			await person.RunGitAsync("commit", "--quiet", "--message", "Remove the file");
			await person.PushAsync("main");
		}

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		File.Delete(Path.Combine(CardSandboxRig.HostPath(card), TempGitRepository.InitialFile));
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
		Assert.Null(await rig.Hub.ReadFileAtAsync("pyxie/card-1", TempGitRepository.InitialFile));
	}

	[Fact]
	public async Task TakeBackResolutionAsync_ForABinaryFileThatConflicted_DoesNotSearchItForMarkers()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CommitFileAsync("data.bin", "the base\0version");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await rig.WorkRoundAsync(1, 1, "data.bin", "the card's\0version");
		await rig.MoveTargetAsync("data.bin", "the target's\0version");
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(card), "data.bin", "<<<<<<< resolved\0bytes");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Null(problem);
	}

	[Fact]
	public async Task TakeBackResolutionAsync_WhenTheCardBranchMovedInTheHubMeanwhile_RefusesTheResolution()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		(_, string targetTip) = await rig.ConflictAsync();
		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Resolve, s_merger);
		await CardSandboxRig.WriteAsync(CardSandboxRig.HostPath(card), TempGitRepository.InitialFile, "resolved with both");
		Assert.Null(await rig.Cards.CommitAsync(card, Message(card), Token));
		await rig.Hub.RunGitAsync("update-ref", "refs/heads/pyxie/card-1", targetTip);

		string? problem = await rig.Cards.TakeBackResolutionAsync(rig.Context, card, Token);

		Assert.Equal("pyxie/card-1 moved in the repository while the round ran.", problem);
		Assert.Equal(targetTip, await rig.HubCardTipAsync(1));
		await AssertNothingLeftAsync(rig, card);
	}
}
