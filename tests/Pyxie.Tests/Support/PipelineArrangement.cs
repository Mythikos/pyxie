using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Arrangements for pipeline tests that go beyond what <see cref="PipelineRig"/> offers: reaching a stage on a later attempt and changing the setup mid-run.</summary>
public static class PipelineArrangement
{
	/// <summary>Creates a rig with one enabled gate, so cards get a gate round. Cards that have a reviewer also get a review round.</summary>
	/// <returns>The rig.</returns>
	public static async Task<PipelineRig> CreateRigWithGateAsync()
	{
		PipelineRig rig = new();
		await rig.AddGateAsync();

		return rig;
	}

	/// <summary>Claims the next card and fails its gates until it is on the wanted attempt, then carries it to the wanted stage. Needs an enabled gate, and a reviewer for the review and merge stages. The card's attempt limit must allow the attempt.</summary>
	/// <param name="rig">The rig whose pipeline runs.</param>
	/// <param name="kind">The stage to stop at; a round of this kind is running when the method returns.</param>
	/// <param name="attempt">The attempt the round belongs to.</param>
	/// <returns>The running round.</returns>
	public static async Task<PipelineWork> ClaimAtAttemptAsync(PipelineRig rig, RoundKind kind, int attempt)
	{
		PipelineWork work = await rig.ClaimAsync();

		for (int current = 1; current < attempt; current++)
		{
			PipelineWork gate = await rig.AdvanceToAsync(work, RoundKind.Gate);
			PipelineWork? sentBack = await rig.RecordAsync(gate, RoundResult.Failed, "Scripted gate failure.");
			Assert.NotNull(sentBack);
			work = sentBack;
		}

		Assert.Equal(attempt, work.Attempt);

		return await rig.AdvanceToAsync(work, kind);
	}

	/// <summary>Connects the repository again with another target branch, so a branch that is hard-coded rather than read from the repository shows.</summary>
	/// <param name="rig">The rig whose repository changes.</param>
	/// <param name="targetBranch">The new target branch.</param>
	/// <returns>A task that completes when the repository is connected.</returns>
	public static async Task MoveTargetBranchAsync(PipelineRig rig, string targetBranch)
	{
		await rig.RemoveRepositoryAsync();
		TestData.ConnectRepository(rig.Database, targetBranch);
	}

	/// <summary>Disables every gate, so gate rounds that open afterwards are recorded as skipped.</summary>
	/// <param name="rig">The rig whose gates change.</param>
	/// <returns>A task that completes when the gates are disabled.</returns>
	public static async Task DisableGatesAsync(PipelineRig rig)
	{
		await using ProjectDbContext context = rig.Database.CreateContext();
		await context.Gates.ExecuteUpdateAsync(changes => changes.SetProperty(gate => gate.IsEnabled, false), TestContext.Current.CancellationToken);
	}
}
