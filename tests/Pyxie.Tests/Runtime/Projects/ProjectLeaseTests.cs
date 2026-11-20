using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Projects;

public sealed class ProjectLeaseTests
{
	[Fact]
	public async Task ProjectChangeWaitsForTheLastLeaseAndAnOldIdCannotAcquireTheNewProject()
	{
		OpenProject open = new();
		open.Set(new Project { Id = 1, Name = "One" });
		PipelineController controller = new(open);
		using ProjectLease first = ResultAssert.Succeeded(controller.AcquireProject(1));
		using ProjectLease second = ResultAssert.Succeeded(controller.AcquireProject(1));
		Task<ServiceResult> change = controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken);
		ResultAssert.Failed(controller.AcquireProject(1));
		Assert.False(controller.TryBeginWork(requireRunning: false), "New recovery work cannot delay a pending change.");
		ResultAssert.Failed(await controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken));
		first.Dispose();
		first.Dispose();
		Assert.False(change.IsCompleted, "The second call still holds the open project.");
		second.Dispose();
		ResultAssert.Succeeded(await change);
		ResultAssert.Failed(controller.AcquireProject(1));
		open.Set(new Project { Id = 2, Name = "Two" });
		controller.EndProjectChange();
		ResultAssert.Failed(controller.AcquireProject(1));
		using ProjectLease current = ResultAssert.Succeeded(controller.AcquireProject(2));
		Assert.Equal(2, current.Project.Id);
	}

	[Fact]
	public async Task CancelledSwitchReleasesItsReservationWhileTheOriginalLeaseIsStillOpen()
	{
		OpenProject open = new();
		open.Set(new Project { Id = 1, Name = "One" });
		PipelineController controller = new(open);
		using ProjectLease first = ResultAssert.Succeeded(controller.AcquireProject(1));
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		Task<ServiceResult> change = controller.BeginProjectChangeAsync(cancellation.Token);
		await cancellation.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);

		using ProjectLease second = ResultAssert.Succeeded(controller.AcquireProject(1));
		Task<ServiceResult> nextChange = controller.BeginProjectChangeAsync(TestContext.Current.CancellationToken);
		Assert.False(nextChange.IsCompleted, "The remaining leases still belong to the first project.");
		first.Dispose();
		second.Dispose();
		ResultAssert.Succeeded(await nextChange.WaitAsync(TestContext.Current.CancellationToken));
		controller.EndProjectChange();
	}
}
