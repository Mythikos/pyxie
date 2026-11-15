using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="ProcessResult.Succeeded"/>, which gates and agents read as "ran and exited with 0".</summary>
public sealed class ProcessResultTests
{
	[Theory]
	[InlineData(ProcessOutcome.Exited, 0, true)]
	[InlineData(ProcessOutcome.Exited, 1, false)]
	[InlineData(ProcessOutcome.Exited, -1, false)]
	[InlineData(ProcessOutcome.TimedOut, 0, false)]
	[InlineData(ProcessOutcome.NotStarted, 0, false)]
	public void Succeeded_IsTrueOnlyForAProcessThatExitedByItselfWithZero(ProcessOutcome outcome, int exitCode, bool expected)
	{
		ProcessResult result = new(outcome, exitCode, string.Empty, TimeSpan.Zero, StartError: null);

		Assert.Equal(expected, result.Succeeded);
	}
}
