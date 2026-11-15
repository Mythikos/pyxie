using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Stopping the pipeline while a round's sandbox is being prepared ends the round: the sandbox it started is let go (a gate, review, or resolve sandbox is removed), and a worker's kept sandbox is picked up again by the card's next work round with what it held.</summary>
public sealed class CardSandboxesCancellationTests
{
	[Theory]
	[InlineData(RoundKind.Gate, SandboxRole.Gates)]
	[InlineData(RoundKind.Review, SandboxRole.Review)]
	[InlineData(RoundKind.Resolve, SandboxRole.Resolve)]
	public async Task OpenAsync_WhenCancelledWhilePreparingAFreshSandbox_RemovesItAndStops(RoundKind kind, SandboxRole role)
	{
		CancellingSandboxes? cancelling = null;
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, decorate: sandboxes => cancelling = new CancellingSandboxes(sandboxes));
		await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		using CancellationTokenSource cancellation = new();
		cancelling?.CancelOnNextStart(cancellation);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Cards.OpenAsync(rig.Context, CardSandboxRig.Work(1, 2, kind), cancellation.Token));

		Assert.Equal(role, rig.Sandboxes.Started[^1].Role);
		Assert.False(Directory.Exists(rig.Sandboxes.WorkFolder(SandboxNames.Container(rig.ProjectKey, 1, role))), "A sandbox that was never handed out must be removed.");
	}

	[Fact]
	public async Task OpenAsync_WhenCancelledWhilePreparingAWorker_LetsTheNextWorkRoundCarryOn()
	{
		CancellingSandboxes? cancelling = null;
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CardSandboxRig rig = await CardSandboxRig.CreateAsync(source, decorate: sandboxes => cancelling = new CancellingSandboxes(sandboxes));
		string committed = await rig.WorkRoundAsync(1, 1, "work.txt", "committed");
		await CardSandboxRig.WriteAsync(rig.WorkerRepository(1), "notes.txt", "left by the last round");
		using CancellationTokenSource cancellation = new();
		cancelling?.CancelOnNextStart(cancellation);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Cards.OpenAsync(rig.Context, CardSandboxRig.Work(1, 2, RoundKind.Work), cancellation.Token));
		await using CardSandbox card = await rig.OpenAsync(1, 3, RoundKind.Work);

		string repository = CardSandboxRig.HostPath(card);
		Assert.Equal(committed, card.BaseCommit);
		Assert.Equal(committed, await rig.HashInAsync(repository));
		Assert.Equal("left by the last round", await CardSandboxRig.ReadAsync(repository, "notes.txt"));
	}
}
