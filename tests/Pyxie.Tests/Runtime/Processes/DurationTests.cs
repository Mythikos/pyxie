using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="Duration.Describe"/>, which words a timeout in the notes a stopped round leaves.</summary>
public sealed class DurationTests
{
	[Theory]
	[InlineData(0, "0.0s")]
	[InlineData(3_400, "3.4s")]
	[InlineData(59_000, "59.0s")]
	[InlineData(60_000, "1 minute")]
	[InlineData(61_000, "1m 1s")]
	[InlineData(61_500, "1m 1s")]
	[InlineData(90_000, "1m 30s")]
	[InlineData(125_000, "2m 5s")]
	[InlineData(120_000, "2 minutes")]
	[InlineData(3_600_000, "60 minutes")]
	[InlineData(5_400_000, "90 minutes")]
	[InlineData(5_401_000, "90m 1s")]
	public void Describe_UsesTheLargestUnitThatKeepsItReadable(int milliseconds, string expected)
	{
		Assert.Equal(expected, Duration.Describe(TimeSpan.FromMilliseconds(milliseconds)));
	}
}
