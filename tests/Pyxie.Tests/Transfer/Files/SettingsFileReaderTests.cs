using System.Text;
using System.Text.RegularExpressions;

using Pyxie.Data.Colors;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;

using Xunit;

using static Pyxie.Tests.Transfer.Files.SettingsFileText;

namespace Pyxie.Tests.Transfer.Files;

/// <summary><see cref="SettingsFileReader"/> reads a file's envelope before the file itself, so a file that is too large, empty, damaged, not a settings file, or written by a newer Pyxie is told apart and refused with the exact message a person sees, which names the line where it can.</summary>
public sealed class SettingsFileReaderTests
{
	private const string NotSettings = "The file is not a Pyxie settings file.";
	private const string Empty = "The file is empty.";
	private const string BadVersion = "The settings file's \"version\" must be a whole number of 1 or more.";
	private const string TooLarge = "The file is larger than 1 MB, so it is not a Pyxie settings file.";

	public static IEnumerable<TheoryDataRow<string, string>> Refusals { get; } =
	[
		Case("an empty file", "", Empty),
		Case("only whitespace", " \t\r\n ", Empty),
		Case("only a byte order mark", "\uFEFF", Empty),
		Case("a byte order mark and whitespace", "\uFEFF \r\n", Empty),
		Case("text that is not JSON", "hello", "The file is not a Pyxie settings file: it cannot be read at line 1."),
		Case("JSON broken on its third line", """
			{
			  "format": "pyxie.project-settings",
			  "version" 1,
			  "categories": []
			}
			""", "The file is not a Pyxie settings file: it cannot be read at line 3."),
		Case("a comment", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  // exported by hand
			  "categories": []
			}
			""", "The file is not a Pyxie settings file: it cannot be read at line 4."),
		Case("a trailing comma after the last member", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": [],
			}
			""", "The file is not a Pyxie settings file: it cannot be read at line 5."),
		Case("a trailing comma after the last item", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": [
			    { "name": "Bug", "color": "#FBE9EC" },
			  ]
			}
			""", "The file is not a Pyxie settings file: it cannot be read at line 6."),
		Case("a list at the root", "[]", NotSettings),
		Case("a string at the root", "\"pyxie.project-settings\"", NotSettings),
		Case("no format", """{ "version": 1, "categories": [] }""", NotSettings),
		Case("another format", """{ "format": "pyxie.settings", "version": 1, "categories": [] }""", NotSettings),
		Case("the format in another case", """{ "format": "Pyxie.Project-Settings", "version": 1, "categories": [] }""", NotSettings),
		Case("a format that is not a string", """{ "format": 1, "version": 1, "categories": [] }""", NotSettings),
		Case("a null format", """{ "format": null, "version": 1, "categories": [] }""", NotSettings),
		Case("no version", """{ "format": "pyxie.project-settings", "categories": [] }""", BadVersion),
		Case("a version written as a string", """{ "format": "pyxie.project-settings", "version": "1", "categories": [] }""", BadVersion),
		Case("a fractional version", """{ "format": "pyxie.project-settings", "version": 1.5, "categories": [] }""", BadVersion),
		Case("version 0", """{ "format": "pyxie.project-settings", "version": 0, "categories": [] }""", BadVersion),
		Case("a negative version", """{ "format": "pyxie.project-settings", "version": -1, "categories": [] }""", BadVersion),
		Case("a null version", """{ "format": "pyxie.project-settings", "version": null, "categories": [] }""", BadVersion),
		Case("a version 4 file with members version 1 does not know", """{ "format": "pyxie.project-settings", "version": 4, "workflows": [ { "name": "Nightly" } ], "agents": [ { "name": "Claude", "model": "opus" } ] }""", "This settings file was exported by a newer Pyxie (file version 4). This Pyxie reads file versions up to 3."),
	];

	[Theory]
	[MemberData(nameof(Refusals))]
	public void Read_RefusesTheFileWithAMessageThatSaysWhatIsWrongWithIt(string text, string expected)
	{
		Assert.Equal(expected, ResultAssert.Failed(Read(text)));
	}

	[Fact]
	public void Read_OfMoreBytesThanTheLimit_RefusesThemBeforeTryingToReadThem()
	{
		byte[] content = new byte[SettingsFileFormat.MaxBytes + 1];
		Array.Fill(content, (byte)'x');

		Assert.Equal(TooLarge, ResultAssert.Failed(SettingsFileReader.Read(content)));
	}

	[Fact]
	public void Read_OfAValidFileOneByteOverTheLimit_RefusesItForItsSize()
	{
		byte[] content = Padded(FileWith(Section("categories", Category("Bug"))), SettingsFileFormat.MaxBytes + 1);

		Assert.Equal(TooLarge, ResultAssert.Failed(SettingsFileReader.Read(content)));
	}

	[Fact]
	public void Read_OfAValidFileOfExactlyTheLimit_ReadsIt()
	{
		byte[] content = Padded(FileWith(Section("categories", Category("Bug"))), SettingsFileFormat.MaxBytes);

		ProjectSettings settings = ResultAssert.Succeeded(SettingsFileReader.Read(content));

		Assert.Equal([new CategoryInput("Bug", CategoryColors.Pink)], settings.Categories);
	}

	[Fact]
	public void Read_OfAFileThatStartsWithAByteOrderMark_ReadsItAsTheSameFileWithout()
	{
		string text = FileWith(Section("agents", Agent("Claude")), Section("categories", Category("Bug")));

		ProjectSettings withMark = ResultAssert.Succeeded(Read("\uFEFF" + text));

		SettingsFileAssert.Same(ResultAssert.Succeeded(Read(text)), withMark);
	}

	[Fact]
	public void Read_OfAMemberRepeatedAtTheRoot_RefusesItNamingTheLineOfTheRepeat()
	{
		string error = ResultAssert.Failed(Read("""
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": [],
			  "version": 1
			}
			"""));

		Assert.True(Regex.IsMatch(error, @"\bline 5\b"), $"A repeated member is refused at its line, as every other unreadable file is, but the message was: {error}");
	}

	[Fact]
	public void Read_OfAMemberRepeatedInAnItem_RefusesItNamingTheLineOfTheRepeat()
	{
		string error = ResultAssert.Failed(Read("""
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": [
			    {
			      "name": "Bug",
			      "color": "#FBE9EC",
			      "name": "Defect"
			    }
			  ]
			}
			"""));

		Assert.True(Regex.IsMatch(error, @"\bline 8\b"), $"A repeated member is refused at its line, as every other unreadable file is, but the message was: {error}");
	}

	private static TheoryDataRow<string, string> Case(string description, string text, string expected)
	{
		return new TheoryDataRow<string, string>(text, expected) { TestDisplayName = description };
	}

	// The file followed by spaces, which JSON ignores, up to an exact number of bytes.
	private static byte[] Padded(string text, int length)
	{
		byte[] file = Encoding.UTF8.GetBytes(text);
		byte[] content = new byte[length];
		Array.Fill(content, (byte)' ');
		file.CopyTo(content, 0);

		return content;
	}
}
