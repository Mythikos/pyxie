using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="CommandLine.IsBatchFile"/> and <see cref="CommandLine.TryBuildBatchCommandLine"/>, which turn arguments into the text cmd.exe runs a batch file with.</summary>
public sealed class CommandLineBatchTests
{
	private const string Batch = @"C:\tools\run.cmd";

	[Theory]
	[InlineData(@"C:\tools\run.cmd", true)]
	[InlineData(@"C:\tools\RUN.CMD", true)]
	[InlineData(@"C:\tools\run.bat", true)]
	[InlineData(@"C:\tools\run.Bat", true)]
	[InlineData(@"C:\tools\run.exe", false)]
	[InlineData(@"C:\tools\run.ps1", false)]
	[InlineData(@"C:\tools\run", false)]
	[InlineData(@"C:\tools\run.cmd.exe", false)]
	public void IsBatchFile_IsTrueForCmdAndBatFilesOnWindowsOnly(string path, bool isBatchFileOnWindows)
	{
		Assert.Equal(isBatchFileOnWindows && OperatingSystem.IsWindows(), CommandLine.IsBatchFile(path));
	}

	[Theory]
	[InlineData(new string[0], @"/d /s /c ""C:\tools\run.cmd""")]
	[InlineData(new[] { "one", "two" }, @"/d /s /c ""C:\tools\run.cmd one two""")]
	[InlineData(new[] { "two words" }, @"/d /s /c ""C:\tools\run.cmd ""two words""""")]
	[InlineData(new[] { "" }, @"/d /s /c ""C:\tools\run.cmd """"""")]
	[InlineData(new[] { @"C:\dir\file.txt" }, @"/d /s /c ""C:\tools\run.cmd C:\dir\file.txt""")]
	[InlineData(new[] { @"C:\dir\" }, @"/d /s /c ""C:\tools\run.cmd C:\dir\""")]
	[InlineData(new[] { @"C:\dir with space\" }, @"/d /s /c ""C:\tools\run.cmd ""C:\dir with space\\""""")]
	[InlineData(new[] { @"C:\dir with space\\" }, @"/d /s /c ""C:\tools\run.cmd ""C:\dir with space\\\\""""")]
	[InlineData(new[] { @"a\b c" }, @"/d /s /c ""C:\tools\run.cmd ""a\b c""""")]
	[InlineData(new[] { "--flag", "value one", "-x" }, @"/d /s /c ""C:\tools\run.cmd --flag ""value one"" -x""")]
	public void TryBuildBatchCommandLine_QuotesArgumentsThatNeedIt(string[] arguments, string expected)
	{
		string? refusal = CommandLine.TryBuildBatchCommandLine(Batch, arguments, out string commandLine);

		Assert.Null(refusal);
		Assert.Equal(expected, commandLine);
	}

	[Fact]
	public void TryBuildBatchCommandLine_ABatchFilePathWithASpace_IsQuoted()
	{
		string? refusal = CommandLine.TryBuildBatchCommandLine(@"C:\my tools\run.cmd", ["x"], out string commandLine);

		Assert.Null(refusal);
		Assert.Equal(@"/d /s /c """"C:\my tools\run.cmd"" x""", commandLine);
	}

	[Theory]
	[InlineData("&")]
	[InlineData("|")]
	[InlineData("<")]
	[InlineData(">")]
	[InlineData("^")]
	[InlineData("(")]
	[InlineData(")")]
	[InlineData(",")]
	[InlineData(";")]
	[InlineData("=")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void TryBuildBatchCommandLine_QuotesAnArgumentWithACharacterCmdTreatsSpecially(string special)
	{
		string argument = $"a{special}b";

		string? refusal = CommandLine.TryBuildBatchCommandLine(Batch, [argument], out string commandLine);

		Assert.Null(refusal);
		Assert.Equal($"/d /s /c \"{Batch} \"{argument}\"\"", commandLine);
	}

	[Theory]
	[InlineData("say \"hi\"")]
	[InlineData("\"")]
	[InlineData("100%")]
	[InlineData("%PATH%")]
	[InlineData("wow!")]
	[InlineData("two\nlines")]
	[InlineData("two\r\nlines")]
	[InlineData("carriage\rreturn")]
	public void TryBuildBatchCommandLine_RefusesAnArgumentCmdCannotPassSafely(string argument)
	{
		string? refusal = CommandLine.TryBuildBatchCommandLine(@"C:\tools\run.cmd", [argument], out string commandLine);

		Assert.NotNull(refusal);
		Assert.Contains("run.cmd", refusal, StringComparison.Ordinal);
		Assert.Contains(argument, refusal, StringComparison.Ordinal);
		Assert.Equal(string.Empty, commandLine);
	}

	[Fact]
	public void TryBuildBatchCommandLine_RefusalAfterSafeArguments_LeavesNoPartialCommandLine()
	{
		string? refusal = CommandLine.TryBuildBatchCommandLine(Batch, ["fine", "also fine", "50%"], out string commandLine);

		Assert.NotNull(refusal);
		Assert.Equal(string.Empty, commandLine);
	}
}
