using Pyxie.Data.Entities;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>The pipeline starts stopped and starts only while a project is open, Start and Stop do nothing when the pipeline is already in that state, every real change is announced once, and the open project changes only while the pipeline is stopped, once it is idle. A pipeline that stops by itself keeps the reason until it starts again; one a person stops has none. The number of running rounds is counted apart from all of that: a change announces itself but never wakes what waits for a start or a stop.</summary>
public sealed class PipelineControllerTests
{
	private const string StopReason = "Docker is not running.";

	[Fact]
	public async Task NewController_IsStoppedAndAnnouncesNothing()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => changes++;

		Assert.False(controller.IsRunning, "Pyxie launches with the pipeline stopped.");
		Assert.Null(controller.StopReason);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task Start_StartsThePipelineAndRaisesStateChangedOnce()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		List<bool> seenRunning = [];
		controller.StateChanged += () => seenRunning.Add(controller.IsRunning);

		controller.Start();

		Assert.True(controller.IsRunning, "Start starts the pipeline.");
		Assert.Equal([true], seenRunning);
	}

	[Fact]
	public async Task Start_WhenAlreadyStarted_DoesNothing()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		int changes = 0;
		controller.StateChanged += () => changes++;
		Task next = controller.NextChangeAsync();

		controller.Start();

		Assert.True(controller.IsRunning, "The pipeline stays started.");
		Assert.Equal(0, changes);
		Assert.False(next.IsCompleted, "A start that changes nothing must not wake the orchestrator.");
	}

	[Fact]
	public async Task Stop_StopsAStartedPipelineAndRaisesStateChangedOnce()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		List<bool> seenRunning = [];
		controller.StateChanged += () => seenRunning.Add(controller.IsRunning);

		controller.Stop();

		Assert.False(controller.IsRunning, "Stop stops the pipeline.");
		Assert.Equal([false], seenRunning);
	}

	[Fact]
	public async Task Stop_WhenAlreadyStopped_DoesNothing()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => changes++;
		Task next = controller.NextChangeAsync();

		controller.Stop();
		controller.Stop();

		Assert.False(controller.IsRunning, "The pipeline stays stopped.");
		Assert.Equal(0, changes);
		Assert.False(next.IsCompleted, "A stop that changes nothing must not wake the orchestrator.");
	}

	[Fact]
	public async Task StopBecause_StopsAStartedPipelineKeepsTheReasonAndRaisesStateChangedOnce()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		List<(bool Running, string? Reason)> seen = [];
		controller.StateChanged += () => seen.Add((controller.IsRunning, controller.StopReason));
		Task next = controller.NextChangeAsync();

		controller.StopBecause(StopReason);

		Assert.False(controller.IsRunning, "The pipeline stops by itself.");
		Assert.Equal(StopReason, controller.StopReason);
		Assert.Equal([(false, StopReason)], seen);
		Assert.True(next.IsCompleted, "Stopping by itself wakes the orchestrator like any stop.");
	}

	[Fact]
	public async Task StopBecause_WhenAlreadyStopped_KeepsNoReasonAndAnnouncesNothing()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => changes++;

		controller.StopBecause(StopReason);

		Assert.False(controller.IsRunning, "The pipeline stays stopped.");
		Assert.Null(controller.StopReason);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task Start_AfterThePipelineStoppedItself_ClearsTheReason()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		controller.StopBecause(StopReason);
		List<string?> seenReasons = [];
		controller.StateChanged += () => seenReasons.Add(controller.StopReason);

		ResultAssert.Succeeded(controller.Start());

		Assert.True(controller.IsRunning, "The pipeline starts again.");
		Assert.Null(controller.StopReason);
		Assert.Equal([null], seenReasons);
	}

	[Fact]
	public async Task Stop_WhenThePipelineAlreadyStoppedItself_KeepsTheReasonUntilItStartsAgain()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		controller.StopBecause(StopReason);
		int changes = 0;
		controller.StateChanged += () => changes++;

		controller.Stop();

		Assert.Equal(StopReason, controller.StopReason);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task Stop_ByAPersonAfterTheRestart_LeavesNoReason()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		controller.StopBecause(StopReason);
		controller.Start();

		controller.Stop();

		Assert.False(controller.IsRunning, "Stop stops the pipeline.");
		Assert.Null(controller.StopReason);
	}

	[Fact]
	public async Task StartStopStart_RaisesStateChangedOncePerChange()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		List<bool> seenRunning = [];
		controller.StateChanged += () => seenRunning.Add(controller.IsRunning);

		controller.Start();
		controller.Start();
		controller.Stop();
		controller.Stop();
		controller.Start();

		Assert.Equal([true, false, true], seenRunning);
	}

	[Fact]
	public async Task Start_FromManyThreadsAtOnce_RaisesStateChangedOnce()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => Interlocked.Increment(ref changes);

		await Parallel.ForAsync(0, 64, TestContext.Current.CancellationToken, (_, _) =>
		{
			controller.Start();

			return ValueTask.CompletedTask;
		});

		Assert.True(controller.IsRunning, "The pipeline is started.");
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task RunningRounds_IsZeroForANewController()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);

		Assert.Equal(0, controller.RunningRounds);
	}

	[Fact]
	public async Task RoundStarted_CountsTheRoundAndRaisesStateChangedOnceWithoutWakingWhatWaitsForAStartOrStop()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		List<int> seen = [];
		controller.StateChanged += () => seen.Add(controller.RunningRounds);
		Task next = controller.NextChangeAsync();

		controller.RoundStarted();

		Assert.Equal(1, controller.RunningRounds);
		Assert.Equal([1], seen);
		Assert.False(next.IsCompleted, "A round that starts is not a start or a stop, so it must not wake the orchestrator.");
		Assert.False(controller.IsRunning, "Counting a round does not start the pipeline.");
	}

	[Fact]
	public async Task RoundEnded_StopsCountingTheRoundAndRaisesStateChangedOnceWithoutWakingWhatWaitsForAStartOrStop()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		controller.RoundStarted();
		controller.RoundStarted();
		List<int> seen = [];
		controller.StateChanged += () => seen.Add(controller.RunningRounds);
		Task next = controller.NextChangeAsync();

		controller.RoundEnded();
		controller.RoundEnded();

		Assert.Equal(0, controller.RunningRounds);
		Assert.Equal([1, 0], seen);
		Assert.False(next.IsCompleted, "A round that ends is not a start or a stop, so it must not wake the orchestrator.");
		Assert.True(controller.IsRunning, "Counting a round does not stop the pipeline.");
	}

	[Fact]
	public async Task StartAndStop_LeaveTheRunningRoundsAlone()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		controller.RoundStarted();

		controller.Stop();

		Assert.Equal(1, controller.RunningRounds);

		controller.Start();
		controller.RoundEnded();

		Assert.Equal(0, controller.RunningRounds);
	}

	[Fact]
	public async Task RoundStartedAndEnded_FromManyThreadsAtOnce_CountEveryRoundAndRaiseStateChangedForEachChange()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => Interlocked.Increment(ref changes);

		await Parallel.ForAsync(0, 64, TestContext.Current.CancellationToken, (_, _) =>
		{
			controller.RoundStarted();
			controller.RoundEnded();

			return ValueTask.CompletedTask;
		});

		Assert.Equal(0, controller.RunningRounds);
		Assert.Equal(128, changes);
	}

	[Fact]
	public async Task NextChangeAsync_CompletesWhenThePipelineIsStartedAndAgainWhenItIsStopped()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		Task started = controller.NextChangeAsync();
		Assert.False(started.IsCompleted, "Nothing has changed yet.");

		controller.Start();
		await started.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		Task stopped = controller.NextChangeAsync();
		Assert.False(stopped.IsCompleted, "A change that already happened does not complete the next wait.");

		controller.Stop();
		await stopped.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task Start_WithoutAnOpenProject_IsRefusedAndAnnouncesNothing()
	{
		await using MachineTestDatabase machine = new();
		PipelineController controller = new(machine.OpenProject);
		int changes = 0;
		controller.StateChanged += () => changes++;

		string error = ResultAssert.Failed(controller.Start());

		Assert.Equal("No project is open.", error);
		Assert.False(controller.IsRunning, "A refused start leaves the pipeline stopped.");
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task BeginProjectChange_WhileThePipelineIsStarted_IsRefused()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();

		Assert.Equal("Stop the pipeline first.", ResultAssert.Failed(await controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task BeginProjectChange_WhileTheStoppedPipelineStillFinishesItsWork_WaitsUntilTheWorkEnds()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		controller.Start();
		Assert.True(controller.TryBeginWork(requireRunning: true), "A started pipeline can begin a session.");
		controller.Stop();

		Task<ServiceResult> change = controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken);
		Assert.False(change.IsCompleted, "The change waits while the session still records its rounds.");
		controller.EndWork();

		ResultAssert.Succeeded(await change.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task BeginProjectChange_WhenThePipelineIsStartedWhileItWaits_IsRefused()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		Assert.True(controller.TryBeginWork(requireRunning: false), "Recovering rounds can begin while the pipeline is stopped.");
		Task<ServiceResult> change = controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken);

		ResultAssert.Succeeded(controller.Start());
		controller.EndWork();

		Assert.Equal("Stop the pipeline first.", ResultAssert.Failed(await change.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)));
	}

	[Fact]
	public async Task StartAndWork_DuringAProjectChange_AreRefusedUntilItEnds()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);
		ResultAssert.Succeeded(await controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken));

		string error = ResultAssert.Failed(controller.Start());
		bool workBegan = controller.TryBeginWork(requireRunning: false);
		string second = ResultAssert.Failed(await controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken));
		controller.EndProjectChange();

		Assert.Equal("A project is being opened or closed. Try again in a moment.", error);
		Assert.False(workBegan, "No work begins while the project changes.");
		Assert.Equal("Another project is being imported, opened, closed, or removed. Try again once it is done.", second);
		ResultAssert.Succeeded(controller.Start());
	}

	[Fact]
	public async Task TryBeginWork_ThatNeedsAStartedPipeline_IsRefusedWhileItIsStopped()
	{
		await using MachineTestDatabase machine = await OpenProjectAsync();
		PipelineController controller = new(machine.OpenProject);

		Assert.False(controller.TryBeginWork(requireRunning: true), "A session needs a started pipeline.");
		Assert.True(controller.TryBeginWork(requireRunning: false), "Recovering rounds does not.");
		controller.EndWork();
	}

	private static async Task<MachineTestDatabase> OpenProjectAsync()
	{
		MachineTestDatabase machine = new();
		Project project = ResultAssert.Succeeded(await machine.Catalog.AddAsync("app", Path.Combine(Path.GetTempPath(), "pyxie-controller-project"), "https://example.test/app.git", TestContext.Current.CancellationToken));
		ResultAssert.Succeeded(await machine.Catalog.OpenAsync(project.Id, TestContext.Current.CancellationToken));

		return machine;
	}
}
