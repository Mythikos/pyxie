using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Git;
using Pyxie.Runtime.Harnesses;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>A card's work and resolve rounds end to end with real Docker, the way <c>RoundExecutor</c> runs them: <see cref="CardSandboxes"/> hands the card in from a real bare hub, <see cref="HarnessRunner"/> runs a shell one-liner as the agent (it reads the prompt, writes a file and <c>.pyxie/result.json</c>), and the work is committed in the sandbox and taken back into the hub as the card's branch. The worker's sandbox is kept between rounds, and removing the card removes everything it left. A resolve round runs a merge agent in a fresh container of its own, in the merge Pyxie started, and the resolution comes back as one merge commit.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class CardSandboxesDockerTests
{
	private const int CardId = 7;
	private const string Briefing = "Write feature.txt.";

	// CommandLine.Split keeps the double-quoted script as one argument; printf's \042 writes the JSON's quotes without nesting them.
	private const string FirstRoundArguments = "-c \"cat > .pyxie/prompt.txt && printf hello > feature.txt && printf '{\\042outcome\\042:\\042completed\\042,\\042note\\042:\\042Wrote feature.txt\\042}' > .pyxie/result.json\"";

	private const string SecondRoundArguments = "-c \"printf ' and more' >> feature.txt && printf '{\\042outcome\\042:\\042completed\\042}' > .pyxie/result.json\"";

	// The card changes README.md, so that the target branch can change the same line, and the merge agent resolves it.
	private const string ConflictingWorkArguments = "-c \"printf 'card version' > README.md && printf '{\\042outcome\\042:\\042completed\\042}' > .pyxie/result.json\"";

	private const string ResolveArguments = "-c \"printf 'resolved version' > README.md && printf '{\\042outcome\\042:\\042resolved\\042,\\042note\\042:\\042Kept both versions\\042}' > .pyxie/result.json\"";

	private static readonly TimeSpan s_agentTimeout = TimeSpan.FromMinutes(2);

	private readonly DockerImageFixture _image;

	public CardSandboxesDockerTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string Branch => PipelineContract.BranchName(CardId);

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WorkRound_ShellAgent_ComesBackAsTheCardBranchAndRemovingTheCardCleansUp()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DockerSandboxRig rig = new(_image);
		await using TempGitRepository hub = await CreateHubAsync(source, rig);
		using ProcessRunner runner = new(TimeProvider.System);
		using RepositoryLock repositoryLock = new();
		using SandboxImageBuilds imageBuilds = new(rig.Sandboxes, TimeProvider.System);
		CardSandboxes cards = new(rig.Sandboxes, imageBuilds, new StandInSignIns(), new GitClient(runner), repositoryLock);
		CardRoundContext context = Context(rig, hub);
		PipelineWork work = Work(sequence: 1, FirstRoundArguments);
		string mainTip = await hub.GetHashAsync(TempGitRepository.MainBranch);

		await using (CardSandbox card = await cards.OpenAsync(context, work, Token))
		{
			RoundVerdict verdict = await RunAgentAsync(work, card, rig);

			Assert.Equal(RoundVerdict.Succeeded("Wrote feature.txt"), verdict);
			Assert.Equal(PipelineContract.Prompt, await card.Sandbox.ReadFileAsync(SandboxPaths.Combine(card.RepositoryPath, ".pyxie/prompt.txt"), 10_000, Token));
			Assert.Equal(Briefing, await card.Sandbox.ReadFileAsync(SandboxPaths.Combine(card.RepositoryPath, PipelineContract.BriefingFile), 10_000, Token));
			await HandInAsync(cards, context, card);
		}

		Assert.Equal("hello", await hub.ReadFileAtAsync(Branch, "feature.txt"));
		Assert.Equal(mainTip, await hub.GetHashAsync($"{Branch}~1"));
		Assert.Equal(mainTip, await hub.GetHashAsync(TempGitRepository.MainBranch));
		string tree = await hub.RunGitAsync("ls-tree", "-r", "--name-only", Branch);
		Assert.False(tree.Contains(PipelineContract.WorkspaceFolder, StringComparison.Ordinal), $"Pyxie's own folder was committed on the card's branch:\n{tree}");
		Assert.Equal("Pyxie", await hub.RunGitAsync("log", "-1", "--format=%an", Branch));
		Assert.False((await hub.TryRunGitAsync("rev-parse", "--quiet", "--verify", $"refs/pyxie/incoming/card-{CardId}")).Succeeded, "The incoming ref was left in the hub.");
		Assert.True(await rig.Docker.ContainerExistsAsync(SandboxNames.Container(rig.ProjectKey, CardId, SandboxRole.Worker)), "The worker's sandbox must be kept for the card's next round.");

		await cards.RemoveAsync(rig.Folder, context.Repository, CardId, Token);

		Assert.False(await rig.Docker.ContainerExistsAsync(SandboxNames.Container(rig.ProjectKey, CardId, SandboxRole.Worker)), "Removing the card left its sandbox.");
		Assert.False(await rig.Docker.VolumeExistsAsync(SandboxNames.Volume(rig.ProjectKey, CardId, "work")), "Removing the card left its work volume.");
		Assert.False(await rig.Docker.VolumeExistsAsync(SandboxNames.Volume(rig.ProjectKey, CardId, "home")), "Removing the card left its home volume.");
		Assert.False(await hub.Git.BranchExistsAsync(hub.Folder, Branch, Token), "Removing the card left its branch in the hub.");
		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.Folder, CardId)), "Removing the card left its sandbox folders on the host.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CommitAsync_WorkerConfiguredAnIdentityInItsRepository_StillCommitsAsPyxie()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DockerSandboxRig rig = new(_image);
		await using TempGitRepository hub = await CreateHubAsync(source, rig);
		using ProcessRunner runner = new(TimeProvider.System);
		using RepositoryLock repositoryLock = new();
		using SandboxImageBuilds imageBuilds = new(rig.Sandboxes, TimeProvider.System);
		CardSandboxes cards = new(rig.Sandboxes, imageBuilds, new StandInSignIns(), new GitClient(runner), repositoryLock);
		CardRoundContext context = Context(rig, hub);

		await using (CardSandbox card = await cards.OpenAsync(context, Work(sequence: 1, FirstRoundArguments), Token))
		{
			foreach ((string key, string value) in new[] { ("author.name", "Configured author"), ("author.email", "author@configured.invalid"), ("committer.name", "Configured committer"), ("committer.email", "committer@configured.invalid") })
			{
				ProcessResult configured = await card.Sandbox.RunAsync(new SandboxCommand("git", ["config", key, value], card.RepositoryPath), Token);
				Assert.True(configured.Succeeded, configured.Output);
			}

			await card.Sandbox.WriteFileAsync(SandboxPaths.Combine(card.RepositoryPath, "work.txt"), "left uncommitted", Token);

			await HandInAsync(cards, context, card);
		}

		Assert.Equal("left uncommitted", await hub.ReadFileAtAsync(Branch, "work.txt"));
		Assert.Equal("Pyxie|pyxie@localhost|Pyxie|pyxie@localhost", await hub.RunGitAsync("log", "-1", "--format=%an|%ae|%cn|%ce", Branch));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task WorkRound_SecondRoundInTheKeptSandbox_BuildsOnTheFirst()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DockerSandboxRig rig = new(_image);
		await using TempGitRepository hub = await CreateHubAsync(source, rig);
		using ProcessRunner runner = new(TimeProvider.System);
		using RepositoryLock repositoryLock = new();
		using SandboxImageBuilds imageBuilds = new(rig.Sandboxes, TimeProvider.System);
		CardSandboxes cards = new(rig.Sandboxes, imageBuilds, new StandInSignIns(), new GitClient(runner), repositoryLock);
		CardRoundContext context = Context(rig, hub);
		string worker = SandboxNames.Container(rig.ProjectKey, CardId, SandboxRole.Worker);

		await using (CardSandbox card = await cards.OpenAsync(context, Work(sequence: 1, FirstRoundArguments), Token))
		{
			Assert.Equal(RoundResult.Succeeded, (await RunAgentAsync(Work(sequence: 1, FirstRoundArguments), card, rig)).Result);
			await HandInAsync(cards, context, card);
		}

		string firstTip = await hub.GetHashAsync(Branch);
		string container = await rig.Docker.ContainerIdAsync(worker);

		await using (CardSandbox card = await cards.OpenAsync(context, Work(sequence: 2, SecondRoundArguments), Token))
		{
			Assert.Equal(firstTip, card.BaseCommit);
			Assert.False(card.BranchIsNew, "The card's branch is in the hub after the first round.");
			Assert.Equal(RoundResult.Succeeded, (await RunAgentAsync(Work(sequence: 2, SecondRoundArguments), card, rig)).Result);
			await HandInAsync(cards, context, card);
		}

		Assert.Equal("hello and more", await hub.ReadFileAtAsync(Branch, "feature.txt"));
		Assert.Equal(firstTip, await hub.GetHashAsync($"{Branch}~1"));
		Assert.Equal(container, await rig.Docker.ContainerIdAsync(worker));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task ResolveRound_ShellAgent_ResolvesTheMergeInAFreshContainerAndComesBackAsOneMergeCommit()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using DockerSandboxRig rig = new(_image);
		await using TempGitRepository hub = await CreateHubAsync(source, rig);
		using ProcessRunner runner = new(TimeProvider.System);
		using RepositoryLock repositoryLock = new();
		using SandboxImageBuilds imageBuilds = new(rig.Sandboxes, TimeProvider.System);
		CardSandboxes cards = new(rig.Sandboxes, imageBuilds, new StandInSignIns(), new GitClient(runner), repositoryLock);
		CardRoundContext context = Context(rig, hub);
		string resolveContainer = SandboxNames.Container(rig.ProjectKey, CardId, SandboxRole.Resolve);

		await using (CardSandbox work = await cards.OpenAsync(context, Work(sequence: 1, ConflictingWorkArguments), Token))
		{
			Assert.Equal(RoundResult.Succeeded, (await RunAgentAsync(Work(sequence: 1, ConflictingWorkArguments), work, rig)).Result);
			await HandInAsync(cards, context, work);
		}

		string cardTip = await hub.GetHashAsync(Branch);
		string targetTip;
		await using (ClonedRepository person = await ClonedRepository.CreateAsync(hub))
		{
			await person.RunGitAsync("checkout", "--quiet", "-B", TempGitRepository.MainBranch, $"origin/{TempGitRepository.MainBranch}");
			targetTip = await person.CommitFileAsync("README.md", "target version");
			await person.PushAsync(TempGitRepository.MainBranch);
		}

		PipelineWork resolve = new(CardId, "Add the feature", RoundId: 2, Sequence: 2, RoundKind.Resolve, Attempt: 2, new AgentSnapshot("Merger", HarnessKind.Other, "sh", ResolveArguments, AgentAuthentication.None), "Resolve README.md.");
		await using (CardSandbox card = await cards.OpenAsync(context, resolve, Token))
		{
			Assert.True(await rig.Docker.ContainerExistsAsync(resolveContainer), "The merge agent runs in a container of its own.");
			ProcessResult status = await card.Sandbox.RunAsync(new SandboxCommand("git", ["status", "--short"], card.RepositoryPath), Token);
			Assert.Contains("UU README.md", status.StandardOutput, StringComparison.Ordinal);

			RoundVerdict verdict = await RunAgentAsync(resolve, card, rig);
			Assert.Equal(RoundVerdict.Succeeded("Kept both versions"), verdict);
			await card.Sandbox.EndProcessesAsync(Token);
			Assert.Null(await cards.CommitAsync(card, $"Merge {TempGitRepository.MainBranch} into {Branch}", Token));
			Assert.Null(await cards.TakeBackResolutionAsync(context, card, Token));
		}

		Assert.Equal($"{cardTip} {targetTip}", await hub.RunGitAsync("log", "-1", "--format=%P", Branch));
		Assert.Equal("resolved version", await hub.ReadFileAtAsync(Branch, "README.md"));
		Assert.Equal(targetTip, await hub.GetHashAsync(TempGitRepository.MainBranch));
		Assert.False(await rig.Docker.ContainerExistsAsync(resolveContainer), "A resolve sandbox is fresh for each round, so it must go when the round ends.");
		Assert.False((await hub.TryRunGitAsync("rev-parse", "--quiet", "--verify", $"refs/pyxie/incoming/card-{CardId}")).Succeeded, "The incoming ref was left in the hub.");

		await cards.RemoveAsync(rig.Folder, context.Repository, CardId, Token);

		Assert.False(Directory.Exists(WorkspaceLayout.CardSandboxesFolder(rig.Folder, CardId)), "Removing the card left its sandbox folders on the host.");
	}

	private static async Task<TempGitRepository> CreateHubAsync(TempGitRepository source, DockerSandboxRig rig)
	{
		string hubPath = WorkspaceLayout.HubPath(rig.Folder);
		await source.RunGitAsync("clone", "--quiet", "--bare", "--", source.Folder, hubPath);

		return await TempGitRepository.AttachAsync(hubPath);
	}

	// The key comes from the project's folder, as RoundExecutor makes it, because CardSandboxes.RemoveAsync finds a card's sandboxes by that key.
	private static CardRoundContext Context(DockerSandboxRig rig, TempGitRepository hub)
	{
		Repository repository = new() { Path = hub.Folder, Origin = hub.Folder, TargetBranch = TempGitRepository.MainBranch };

		return new CardRoundContext(rig.Folder, SandboxNames.ProjectKey(rig.Folder), repository, SandboxDefaults.Dockerfile);
	}

	private static PipelineWork Work(int sequence, string arguments)
	{
		return new PipelineWork(CardId, "Add the feature", sequence, sequence, RoundKind.Work, 1, new AgentSnapshot("Shell", HarnessKind.Other, "sh", arguments, AgentAuthentication.None), Briefing);
	}

	private static Task<RoundVerdict> RunAgentAsync(PipelineWork work, CardSandbox card, DockerSandboxRig rig)
	{
		return new HarnessRunner().RunAsync(work, card.Sandbox, card.RepositoryPath, WorkspaceLayout.RoundLogPath(rig.Folder, CardId, work.Sequence), s_agentTimeout, Token);
	}

	private static async Task HandInAsync(CardSandboxes cards, CardRoundContext context, CardSandbox card)
	{
		await card.Sandbox.EndProcessesAsync(Token);

		Assert.Null(await cards.CommitAsync(card, "Add the feature", Token));
		Assert.Null(await cards.TakeBackAsync(context, card, Token));
	}
}
