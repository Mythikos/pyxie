using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerCli.LastLine"/>, which picks out the line where docker says what went wrong, for the messages people see when a sandbox fails.</summary>
public sealed class DockerCliTests
{
	[Theory]
	[InlineData("Unable to find image\ndocker: Error response from daemon: pull access denied.\n", "docker: Error response from daemon: pull access denied.")]
	[InlineData("first\r\n  second  \r\n\r\n  \n", "second")]
	[InlineData("only line", "only line")]
	public void LastLine_Output_IsTheLastLineWithText(string output, string expected)
	{
		Assert.Equal(expected, DockerCli.LastLine(output));
	}

	[Theory]
	[InlineData("")]
	[InlineData("\n \n\t\n")]
	public void LastLine_NoText_SaysNoReasonWasGiven(string output)
	{
		Assert.Equal("no reason given.", DockerCli.LastLine(output));
	}
}
