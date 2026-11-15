using System.Text;

using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="SandboxImageFiles"/>: every file of the base image's folder is embedded in Pyxie, read with Unix line endings because the scripts run in Linux, and the hash that tags the base image covers every file's name and text, so a Pyxie with different files builds its own base image instead of reusing a stale one.</summary>
public sealed class SandboxImageFilesTests
{
	private const string Dockerfile = "Dockerfile";

	private static string ImageFolder => Path.Combine(RepositoryLayout.FindRoot(), "src", "Pyxie.Runtime", "Sandboxes", "Image");

	[Fact]
	public void Files_ImageFolder_EveryFileIsEmbedded()
	{
		string[] onDisk = [.. Directory.EnumerateFiles(ImageFolder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];

		Assert.Equal(onDisk, SandboxImageFiles.Files.Keys.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Files_EachFile_IsTheTextOnDiskWithUnixLineEndings()
	{
		foreach ((string name, string text) in SandboxImageFiles.Files)
		{
			string onDisk = File.ReadAllText(Path.Combine(ImageFolder, name), Encoding.UTF8).ReplaceLineEndings("\n");

			Assert.True(onDisk == text, $"The embedded {name} is not the file in {ImageFolder}.");
		}
	}

	[Fact]
	public void Files_EachFile_HasNoCarriageReturnOrByteOrderMark()
	{
		foreach ((string name, string text) in SandboxImageFiles.Files)
		{
			Assert.False(text.Contains('\r', StringComparison.Ordinal), $"{name} has a carriage return, which breaks a shell script or Dockerfile in Linux.");
			Assert.False(text.StartsWith('﻿'), $"{name} starts with a byte order mark, which the shell would read as part of the first line.");
		}
	}

	[Fact]
	public void Files_EachScript_StartsWithAShellLine()
	{
		foreach ((string name, string text) in SandboxImageFiles.Files.Where(file => file.Key != Dockerfile))
		{
			Assert.True(text.StartsWith("#!/bin/sh\n", StringComparison.Ordinal), $"{name} does not start with #!/bin/sh, so the container cannot run it.");
		}
	}

	[Fact]
	public void Files_Dockerfile_CopiesEveryScriptIntoTheImage()
	{
		string copy = SandboxImageFiles.Files[Dockerfile].Split('\n').Single(line => line.StartsWith("COPY ", StringComparison.Ordinal));
		string[] copied = copy.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..^1];

		Assert.Equal(SandboxImageFiles.Files.Keys.Where(name => name != Dockerfile).Order(StringComparer.Ordinal), copied.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void Hash_Files_IsSixteenHexadecimalCharactersThatStayTheSame()
	{
		Assert.Matches("^[0-9a-f]{16}$", SandboxImageFiles.Hash);
		Assert.Equal(SandboxImageFiles.Hash, SandboxImageFiles.Hash);
	}

	// SandboxImageFiles has no seam to hash other files, so this pins the formula: the hash is computed over every file's name and text in name order, which is what makes it change whenever any file does.
	[Fact]
	public void Hash_Files_CoversEveryFileNameAndText()
	{
		Dictionary<string, string> files = new(SandboxImageFiles.Files, StringComparer.Ordinal);

		Assert.Equal(HashOf(files), SandboxImageFiles.Hash);
		foreach (string name in files.Keys)
		{
			Dictionary<string, string> changedText = new(files, StringComparer.Ordinal) { [name] = files[name] + "# changed\n" };
			Dictionary<string, string> renamed = new(files.Where(file => file.Key != name), StringComparer.Ordinal) { [name + "-renamed"] = files[name] };

			Assert.True(HashOf(changedText) != SandboxImageFiles.Hash, $"Changing the text of {name} must change the base image's tag.");
			Assert.True(HashOf(renamed) != SandboxImageFiles.Hash, $"Renaming {name} must change the base image's tag.");
		}
	}

	private static string HashOf(IReadOnlyDictionary<string, string> files)
	{
		StringBuilder all = new();
		foreach ((string name, string text) in files.OrderBy(file => file.Key, StringComparer.Ordinal))
		{
			all.Append(name).Append('\0').Append(text).Append('\0');
		}

		return SandboxNames.Hash(all.ToString())[..16];
	}
}
