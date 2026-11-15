using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="CommandLine.Resolve"/> for names looked up in <c>PATH</c> folders and extended with <c>PATHEXT</c> on Windows. Each test sets those variables for itself and puts them back, which is only safe because <see cref="ProcessEnvironmentCollection"/> never runs beside another test.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class CommandLineResolvePathTests
{
	private const string Executables = ".EXE;.CMD";

	// What a program called "tool" is named on this platform: Windows adds a PATHEXT extension to the name, elsewhere the name is the file.
	private static string ToolFile => OperatingSystem.IsWindows() ? "tool.exe" : "tool";

	[Fact]
	public async Task Resolve_ANameInAPathFolder_ReturnsTheFileThere()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile(Path.Combine("bin", ToolFile));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		AssertResolved(tool, CommandLine.Resolve("tool", scratch.Folder));
	}

	[Fact]
	public async Task Resolve_ANameInSeveralPathFolders_ReturnsTheFirstFolderThatHasIt()
	{
		await using ScratchFolder scratch = new();
		string first = scratch.CreateFile(Path.Combine("first", ToolFile));
		string second = scratch.CreateFile(Path.Combine("second", ToolFile));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		using (PathOf(scratch.Combine("first"), scratch.Combine("second")))
		{
			AssertResolved(first, CommandLine.Resolve("tool", scratch.Folder));
		}

		using (PathOf(scratch.Combine("second"), scratch.Combine("first")))
		{
			AssertResolved(second, CommandLine.Resolve("tool", scratch.Folder));
		}
	}

	[Fact]
	public async Task Resolve_ANameMissingFromEarlierPathFolders_IsFoundInALaterOne()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile(Path.Combine("later", ToolFile));
		scratch.CreateSubfolder("earlier");
		using EnvironmentVariableScope path = PathOf(scratch.Combine("earlier"), scratch.Combine("missing"), scratch.Combine("later"));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		AssertResolved(tool, CommandLine.Resolve("tool", scratch.Folder));
	}

	[Fact]
	public async Task Resolve_PathEntriesThatAreQuotedPaddedOrEmpty_AreStillSearched()
	{
		await using ScratchFolder scratch = new();
		string quoted = scratch.CreateFile(Path.Combine("quoted folder", ToolFile));
		string padded = scratch.CreateFile(Path.Combine("padded", OperatingSystem.IsWindows() ? "other.exe" : "other"));
		string separator = Path.PathSeparator.ToString();
		using EnvironmentVariableScope path = new("PATH", string.Join(separator, string.Empty, $"\"{scratch.Combine("quoted folder")}\"", string.Empty, $"  {scratch.Combine("padded")}  ", string.Empty));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		AssertResolved(quoted, CommandLine.Resolve("tool", scratch.Folder));
		AssertResolved(padded, CommandLine.Resolve("other", scratch.Folder));
	}

	[Fact]
	public async Task Resolve_AnEmptyPathEntry_DoesNotSearchTheCurrentDirectory()
	{
		await using ScratchFolder scratch = new();
		string name = $"pyxie-in-cwd-{Guid.NewGuid():N}";
		string program = Path.Combine(Environment.CurrentDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name);
		await File.WriteAllTextAsync(program, string.Empty, TestContext.Current.CancellationToken);

		try
		{
			using EnvironmentVariableScope path = PathOf(string.Empty, scratch.CreateSubfolder("elsewhere"), string.Empty);
			using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

			Assert.Null(CommandLine.Resolve(name, scratch.Folder));
		}
		finally
		{
			File.Delete(program);
		}
	}

	[Fact]
	public async Task Resolve_WithNoPath_FindsNothing()
	{
		await using ScratchFolder scratch = new();
		scratch.CreateFile(Path.Combine("bin", ToolFile));
		using EnvironmentVariableScope path = new("PATH", null);
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		Assert.Null(CommandLine.Resolve("tool", scratch.Folder));
	}

	[Fact]
	public async Task Resolve_ANameThatAlreadyHasItsExtension_IsFoundOnPath()
	{
		await using ScratchFolder scratch = new();
		string tool = scratch.CreateFile(Path.Combine("bin", ToolFile));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		AssertResolved(tool, CommandLine.Resolve(ToolFile, scratch.Folder));
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_TriesThePathExtensionsInTheOrderTheyAreListed()
	{
		await using ScratchFolder scratch = new();
		string exe = scratch.CreateFile(Path.Combine("bin", "tool.exe"));
		string cmd = scratch.CreateFile(Path.Combine("bin", "tool.cmd"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));

		using (new EnvironmentVariableScope("PATHEXT", ".EXE;.CMD"))
		{
			AssertResolved(exe, CommandLine.Resolve("tool", scratch.Folder));
		}

		using (new EnvironmentVariableScope("PATHEXT", ".CMD;.EXE"))
		{
			AssertResolved(cmd, CommandLine.Resolve("tool", scratch.Folder));
		}
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_UsesTheExtensionsPathextListsAndNoOthers()
	{
		await using ScratchFolder scratch = new();
		string text = scratch.CreateFile(Path.Combine("bin", "tool.txt"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));

		using (new EnvironmentVariableScope("PATHEXT", ".EXE;.CMD"))
		{
			Assert.Null(CommandLine.Resolve("tool", scratch.Folder));
			Assert.Null(CommandLine.Resolve("tool.txt", scratch.Folder));
		}

		using (new EnvironmentVariableScope("PATHEXT", ".EXE;.TXT"))
		{
			AssertResolved(text, CommandLine.Resolve("tool", scratch.Folder));
		}
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_ToleratesPaddingAndEmptyEntriesInPathext()
	{
		await using ScratchFolder scratch = new();
		string cmd = scratch.CreateFile(Path.Combine("bin", "tool.cmd"));

		// An empty entry must not be read as "no extension", or the extensionless file would be taken for the program.
		scratch.CreateFile(Path.Combine("bin", "tool"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", " .EXE ; ;.CMD ;;");

		AssertResolved(cmd, CommandLine.Resolve("tool", scratch.Folder));
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_WithoutPathext_FallsBackToTheUsualExecutableExtensions()
	{
		await using ScratchFolder scratch = new();
		string bat = scratch.CreateFile(Path.Combine("bin", "tool.bat"));
		scratch.CreateFile(Path.Combine("bin", "script.vbs"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", null);

		AssertResolved(bat, CommandLine.Resolve("tool", scratch.Folder));
		Assert.Null(CommandLine.Resolve("script", scratch.Folder));
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_IgnoresAFileWithoutAnExtension()
	{
		await using ScratchFolder scratch = new();
		string bare = scratch.CreateFile(Path.Combine("bin", "tool"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		Assert.Null(CommandLine.Resolve("tool", scratch.Folder));
		Assert.Null(CommandLine.Resolve(bare, scratch.Folder));

		string cmd = scratch.CreateFile(Path.Combine("bin", "tool.cmd"));

		AssertResolved(cmd, CommandLine.Resolve("tool", scratch.Folder));
		AssertResolved(cmd, CommandLine.Resolve(bare, scratch.Folder));
	}

	[Fact(Skip = HostPlatform.NeedsWindows, SkipUnless = nameof(HostPlatform.IsWindows), SkipType = typeof(HostPlatform))]
	public async Task Resolve_OnWindows_ADottedNameStillGetsAnExtension()
	{
		await using ScratchFolder scratch = new();
		string exe = scratch.CreateFile(Path.Combine("bin", "tool.v2.exe"));
		using EnvironmentVariableScope path = PathOf(scratch.Combine("bin"));
		using EnvironmentVariableScope extensions = new("PATHEXT", Executables);

		AssertResolved(exe, CommandLine.Resolve("tool.v2", scratch.Folder));
		AssertResolved(exe, CommandLine.Resolve(scratch.Combine(Path.Combine("bin", "tool.v2")), scratch.Folder));

		File.Delete(exe);
		scratch.CreateFile(Path.Combine("bin", "tool.v2"));

		Assert.Null(CommandLine.Resolve("tool.v2", scratch.Folder));
	}

	// PATHEXT supplies the extension in the case it is written in, so the same file can come back as tool.EXE.
	private static void AssertResolved(string expected, string? actual)
	{
		Assert.Equal(expected, actual, ignoreCase: OperatingSystem.IsWindows());
	}

	private static EnvironmentVariableScope PathOf(params string[] folders)
	{
		return new EnvironmentVariableScope("PATH", string.Join(Path.PathSeparator, folders));
	}
}
