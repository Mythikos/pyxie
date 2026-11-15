using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="CommandLine.Resolve"/> for paths and for the name of a program every machine has. Looking a name up in folders the test puts on <c>PATH</c> changes the process environment, so <see cref="CommandLineResolvePathTests"/> does that.</summary>
public sealed class CommandLineResolveTests
{
	// What an executable file is called on this platform: Windows runs only files with an extension, elsewhere the name alone is the file.
	private static string ExecutableName => OperatingSystem.IsWindows() ? "tool.exe" : "tool";

	[Fact]
	public async Task Resolve_AFullPathToAnExistingFile_ReturnsThatPath()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile(ExecutableName);

		Assert.Equal(tool, CommandLine.Resolve(tool, scratch.CreateSubfolder("elsewhere")));
	}

	[Fact]
	public async Task Resolve_ARelativePath_IsResolvedAgainstTheWorkingDirectory()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile(Path.Combine("bin", ExecutableName));

		Assert.Equal(tool, CommandLine.Resolve(Path.Combine("bin", ExecutableName), scratch.Folder));
		Assert.Equal(tool, CommandLine.Resolve($"bin/{ExecutableName}", scratch.Folder));
		Assert.Equal(tool, CommandLine.Resolve(Path.Combine("..", "bin", ExecutableName), scratch.CreateSubfolder("other")));
		Assert.Equal(tool, CommandLine.Resolve(Path.Combine(".", ExecutableName), Path.GetDirectoryName(tool) ?? string.Empty));
	}

	[Fact]
	public async Task Resolve_ARelativePathToAMissingFile_ReturnsNull()
	{
		await using ScratchFolder scratch = new();
		scratch.CreateFile(ExecutableName);

		Assert.Null(CommandLine.Resolve(Path.Combine("bin", ExecutableName), scratch.Folder));
	}

	[Fact]
	public async Task Resolve_ABareName_IsNotLookedUpInTheWorkingDirectory()
	{
		await using ScratchFolder scratch = new();
		string name = $"pyxie-only-here-{Guid.NewGuid():N}";
		scratch.CreateFile(OperatingSystem.IsWindows() ? name + ".exe" : name);

		Assert.Null(CommandLine.Resolve(name, scratch.Folder));
	}

	[Fact]
	public async Task Resolve_APathToAFolder_ReturnsNull()
	{
		await using ScratchFolder scratch = new();
		string folderNamedLikeATool = scratch.CreateSubfolder(ExecutableName);

		Assert.Null(CommandLine.Resolve(folderNamedLikeATool, scratch.Folder));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public async Task Resolve_ABlankCommand_ReturnsNull(string command)
	{
		await using ScratchFolder scratch = new();

		Assert.Null(CommandLine.Resolve(command, scratch.Folder));
	}

	[Fact]
	public async Task Resolve_TheNameOfAProgramOnPath_ReturnsTheExistingFile()
	{
		await using ScratchFolder scratch = new();

		string? dotnet = CommandLine.Resolve("dotnet", scratch.Folder);

		Assert.True(dotnet is not null, "dotnet is on PATH wherever these tests run, because the fake harness agents are started through it.");
		Assert.True(File.Exists(dotnet), $"Resolve returned {dotnet}, which is not a file.");
		Assert.True(Path.IsPathRooted(dotnet), $"Resolve returned {dotnet}, which is not a full path.");
		Assert.Equal("dotnet", Path.GetFileNameWithoutExtension(dotnet));
	}

	[Fact]
	public async Task Resolve_ANameThatIsNotOnPath_ReturnsNull()
	{
		await using ScratchFolder scratch = new();

		Assert.Null(CommandLine.Resolve($"pyxie-no-such-program-{Guid.NewGuid():N}", scratch.Folder));
	}

	[Fact(Skip = HostPlatform.NeedsAnotherPlatform, SkipUnless = nameof(HostPlatform.IsNotWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OutsideWindows_AFileWithoutAnExtensionIsAProgram()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile("tool");

		Assert.Equal(tool, CommandLine.Resolve(tool, scratch.Folder));
	}
}
