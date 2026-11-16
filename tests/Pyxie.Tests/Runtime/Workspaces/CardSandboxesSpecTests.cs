using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>What each round's sandbox is made with and handed: the project's image, its role's volumes and host folders, the agent's sign-in, and a bundle of only the target branch and the card's own branch. A round whose image or sign-in is missing starts nothing.</summary>
public sealed class CardSandboxesSpecTests
{
	private static readonly AgentSnapshot s_claude = new("Claude", HarnessKind.ClaudeCode, "claude", "-p");

	private static SandboxRole RoleOf(RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work => SandboxRole.Worker,
			RoundKind.Gate => SandboxRole.Gates,
			RoundKind.Resolve => SandboxRole.Resolve,
			_ => SandboxRole.Review,
		};
	}

	private static IReadOnlyList<SandboxVolume> VolumesOf(string projectKey, SandboxRole role)
	{
		return role switch
		{
			SandboxRole.Worker =>
			[
				new SandboxVolume(SandboxNames.Volume(projectKey, 1, "work"), "/work"),
				new SandboxVolume(SandboxNames.Volume(projectKey, 1, "home"), "/home/node"),
			],
			SandboxRole.Gates => [new SandboxVolume(SandboxNames.Volume(projectKey, 1, "gates-home"), "/home/node")],
			_ => [],
		};
	}

	// A resolve round starts a merge, which needs something to merge, so the target branch moves on while the card's work waits.
	private static async Task PrepareCardAsync(CardSandboxRig rig, RoundKind kind)
	{
		await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		if (kind == RoundKind.Resolve)
		{
			await rig.MoveTargetAsync("moved-on.txt", "the target moved on");
		}
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public async Task OpenAsync_StartsTheRolesSandboxWithItsVolumesAndHostFolders(RoundKind kind)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await PrepareCardAsync(rig, kind);
		SandboxRole role = RoleOf(kind);

		await using CardSandbox card = await rig.OpenAsync(1, 2, kind);

		SandboxSpec spec = rig.Sandboxes.Started[^1];
		string folder = rig.SandboxFolder(1, role);
		Assert.Equal(SandboxNames.Container(rig.ProjectKey, 1, role), spec.Name);
		Assert.Equal(LocalSandboxes.Image, spec.Image);
		Assert.Equal(rig.ProjectKey, spec.ProjectKey);
		Assert.Equal(1, spec.CardId);
		Assert.Equal(role, spec.Role);
		Assert.Equal(Path.Combine(folder, "in"), spec.InputFolder);
		Assert.Equal(Path.Combine(folder, "out"), spec.OutputFolder);
		Assert.Equal(folder, card.Folder);
		Assert.Equal(VolumesOf(rig.ProjectKey, role), spec.Volumes);
		Assert.Equal(role == SandboxRole.Worker, spec.Keep);
		Assert.Empty(spec.Secrets);
		Assert.Empty(spec.Mounts);
		Assert.Empty(spec.Environment);
	}

	[Fact]
	public async Task OpenAsync_ForANewCardBranch_HandsInOnlyTheTargetBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature");
		await source.CreateBranchAsync("pyxie/card-2");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);

		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);

		Assert.Equal(["refs/heads/main"], await rig.HandInRefsAsync(1, SandboxRole.Worker));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public async Task OpenAsync_HandsInTheTargetAndTheCardsOwnBranchAndNoOther(RoundKind kind)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature");
		await source.CreateBranchAsync("pyxie/card-2");
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source);
		await PrepareCardAsync(rig, kind);

		await using CardSandbox card = await rig.OpenAsync(1, 2, kind);

		Assert.Equal(["refs/heads/main", "refs/heads/pyxie/card-1"], await rig.HandInRefsAsync(1, RoleOf(kind)));
		string remotes = await rig.RunGitInAsync(CardSandboxRig.HostPath(card), "for-each-ref", "--format=%(refname)", "refs/remotes");
		Assert.Equal(["refs/remotes/origin/main", "refs/remotes/origin/pyxie/card-1"], remotes.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries));
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public async Task OpenAsync_ForAnAgentsRound_GivesItsSandboxTheAgentsSignIn(RoundKind kind)
	{
		SandboxSignIn signIn = new([new SandboxSecret(Path.Combine(Path.GetTempPath(), "claude-oauth-token"), "CLAUDE_CODE_OAUTH_TOKEN")], [new SandboxMount(Path.Combine(Path.GetTempPath(), "codex"), "/home/node/.codex-pyxie", ReadOnly: false)], new Dictionary<string, string> { ["CODEX_HOME"] = "/home/node/.codex-pyxie" }, Problem: null);
		SettableSignIns signIns = new(signIn);
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, signIns: signIns);
		await PrepareCardAsync(rig, kind);
		SandboxRole role = RoleOf(kind);

		await using CardSandbox card = await rig.OpenAsync(1, 2, kind, s_claude);

		SandboxSpec spec = rig.Sandboxes.Started[^1];
		Assert.Equal((HarnessKind.ClaudeCode, Path.Combine(rig.SandboxFolder(1, role), "sign-in")), Assert.Single(signIns.Requests));
		Assert.Equal(signIn.Secrets, spec.Secrets);
		Assert.Equal(signIn.Mounts, spec.Mounts);
		Assert.Equal(signIn.Environment, spec.Environment);
	}

	[Fact]
	public async Task OpenAsync_ForAGateRound_AsksForNoSignIn()
	{
		SettableSignIns signIns = new(SandboxSignIn.Missing("Gates must never ask for a sign-in."));
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, signIns: signIns);
		await rig.WorkRoundAsync(1, 1, "work.txt", "committed");

		await using CardSandbox card = await rig.OpenAsync(1, 2, RoundKind.Gate, s_claude);

		Assert.Empty(signIns.Requests);
		Assert.Empty(rig.Sandboxes.Started[^1].Secrets);
	}

	[Theory]
	[InlineData(RoundKind.Work)]
	[InlineData(RoundKind.Review)]
	[InlineData(RoundKind.Resolve)]
	public async Task OpenAsync_WhenTheAgentCannotSignIn_ThrowsASandboxExceptionWithTheReasonAndStartsNothing(RoundKind kind)
	{
		const string Problem = "Claude Code has no token: run claude setup-token and save it in the secrets folder.";
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, signIns: new SettableSignIns(SandboxSignIn.Missing(Problem)));
		await PrepareCardAsync(rig, kind);
		int started = rig.Sandboxes.Started.Count;

		SandboxException failure = await Assert.ThrowsAsync<SandboxException>(() => rig.OpenAsync(1, 2, kind, s_claude));

		Assert.Equal(Problem, failure.Message);
		Assert.Equal(started, rig.Sandboxes.Started.Count);
	}

	[Fact]
	public async Task OpenAsync_AsksForTheProjectsImageAndStartsTheSandboxFromIt()
	{
		SettableImageSandboxes? images = null;
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, decorate: sandboxes => images = new SettableImageSandboxes(sandboxes, ServiceResult<string>.Success("pyxie-project-test:abc")));

		await using CardSandbox card = await rig.OpenAsync(1, 1, RoundKind.Work);

		Assert.Equal("pyxie-project-test:abc", rig.Sandboxes.Started[^1].Image);
		SandboxImageRequest request = Assert.Single(images?.Requests ?? []);
		Assert.NotNull(request.BuildStarting);
		Assert.Equal(new SandboxImageRequest(rig.ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, WorkspaceLayout.ImageLogPath(rig.ProjectFolder)), request with { BuildStarting = null });
	}

	[Fact]
	public async Task OpenAsync_WhenTheImageCannotBeBuilt_ThrowsASandboxExceptionWithTheReasonAndStartsNothing()
	{
		const string Problem = "The project's image did not build: step 3 of the Dockerfile failed.";
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, decorate: sandboxes => new SettableImageSandboxes(sandboxes, ServiceResult<string>.Failure(Problem)));

		SandboxException failure = await Assert.ThrowsAsync<SandboxException>(() => rig.OpenAsync(1, 1, RoundKind.Work));

		Assert.Equal(Problem, failure.Message);
		Assert.Empty(rig.Sandboxes.Started);
	}
}
