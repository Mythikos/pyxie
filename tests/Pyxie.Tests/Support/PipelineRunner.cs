using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Runs the pipeline the way an orchestrator would, reporting each round as a script says, for tests about what the whole loop does rather than one transition.</summary>
public static class PipelineRunner
{
	// A bound that fails the test when a rule that should stop a card does not, instead of letting the test run forever.
	private const int MaxRounds = 200;

	/// <summary>Claims and runs rounds until nothing is left to claim: every card has completed or stopped.</summary>
	/// <param name="rig">The rig whose pipeline runs.</param>
	/// <param name="decide">What each round reports, by its kind. A result other than success is reported with a note.</param>
	/// <returns>The number of rounds that ran.</returns>
	public static async Task<int> RunUntilIdleAsync(PipelineRig rig, Func<RoundKind, RoundResult> decide)
	{
		int rounds = 0;
		PipelineWork? work = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);

		while (work is not null)
		{
			rounds++;
			Assert.True(rounds <= MaxRounds, $"The pipeline ran more than {MaxRounds} rounds: nothing stopped the card.");

			RoundResult result = decide(work.Kind);
			work = await rig.RecordAsync(work, result, result == RoundResult.Succeeded ? null : $"Scripted {result}.");
			work ??= await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);
		}

		return rounds;
	}

	/// <summary>Gets a script in which every round succeeds except the rounds of one kind, which always report the same result.</summary>
	/// <param name="failingKind">The kind of round that does not succeed.</param>
	/// <param name="failure">What those rounds report.</param>
	/// <returns>The script for <see cref="RunUntilIdleAsync"/>.</returns>
	public static Func<RoundKind, RoundResult> FailAt(RoundKind failingKind, RoundResult failure)
	{
		return kind => kind == failingKind ? failure : RoundResult.Succeeded;
	}
}
