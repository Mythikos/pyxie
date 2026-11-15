using Pyxie.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary><see cref="CommandLine.Split"/> against the rules Windows programs parse their command line with. In the cases below <c>~</c> stands for a double quote, which keeps the quoting readable. The rules for a doubled quote inside quotes are those of the C runtime that the programs Pyxie starts (dotnet, node, git) use, checked against a real dotnet process.</summary>
public sealed class CommandLineSplitTests
{
	public static IEnumerable<TheoryDataRow<string, string[]>> Cases { get; } =
	[
		Case("empty text has no arguments", "", []),
		Case("only whitespace has no arguments", " \t\r\n ", []),
		Case("a single word", "plain", ["plain"]),
		Case("runs of spaces separate arguments", "a b   c", ["a", "b", "c"]),
		Case("a tab separates arguments", "a\tb", ["a", "b"]),
		Case("a line feed separates arguments", "a\nb", ["a", "b"]),
		Case("a carriage return and line feed separate arguments", "a\r\nb", ["a", "b"]),
		Case("leading and trailing whitespace is ignored", "  a b \t", ["a", "b"]),
		Case("quotes group words into one argument", "~abc def~ ghi", ["abc def", "ghi"]),
		Case("quotes may enclose a whole argument", "~abc~ d e", ["abc", "d", "e"]),
		Case("quotes may start in the middle of an argument", "a~b c~d", ["ab cd"]),
		Case("text after a closing quote continues the argument", "~a b~c", ["a bc"]),
		Case("a tab inside quotes is kept", "~a\tb~ c", ["a\tb", "c"]),
		Case("a line feed inside quotes is kept", "~a\nb~ c", ["a\nb", "c"]),
		Case("a carriage return and line feed inside quotes are kept", "~a\r\nb~", ["a\r\nb"]),
		Case("an empty quoted argument is an empty string", "~~", [""]),
		Case("an empty quoted argument between others", "a ~~ b", ["a", "", "b"]),
		Case("two empty quoted arguments", "~~ ~~", ["", ""]),
		Case("adjacent quotes inside an unquoted argument vanish", "a~~b", ["ab"]),
		Case("a doubled quote inside quotes is one literal quote", "~a ~~quoted~~ word~", ["a ~quoted~ word"]),
		Case("a doubled quote after quoted text stays inside the quotes", "a~b~~ c d", ["ab~ c d"]),
		Case("three quotes around a word give a quoted word", "~~~CallMeIshmael~~~  b  c", ["~CallMeIshmael~", "b", "c"]),
		Case("three quotes around words keep the spaces", "~~~Call Me Ishmael~~~", ["~Call Me Ishmael~"]),
		Case("an unterminated quote runs to the end", "~unterminated start", ["unterminated start"]),
		Case("backslashes in a path are literal", @"C:\dir\file.txt", [@"C:\dir\file.txt"]),
		Case("a trailing backslash is literal", @"C:\dir\", [@"C:\dir\"]),
		Case("a double backslash without quotes stays double", @"\\server\share", [@"\\server\share"]),
		Case("backslashes before other characters are literal", @"a\\\b d~e f~g h", [@"a\\\b", "de fg", "h"]),
		Case("a backslash before a quote makes the quote literal", @"a\~b c d", ["a~b", "c", "d"]),
		Case("three backslashes before a quote give one backslash and a literal quote", @"a\\\~b c d", [@"a\~b", "c", "d"]),
		Case("two backslashes before a quote give one backslash and the quote still groups", @"a\\~b c~ d e", [@"a\b c", "d", "e"]),
		Case("four backslashes before a quote give two backslashes and the quote still groups", @"a\\\\~b c~ d e", [@"a\\b c", "d", "e"]),
		Case("a backslash before the closing quote of a path swallows the quote", @"~C:\dir with space\~", [@"C:\dir with space~"]),
		Case("two backslashes before the closing quote of a path keep one backslash", @"~C:\dir with space\\~", [@"C:\dir with space\"]),
		Case("escaped quotes around a word", @"\~quoted\~", ["~quoted~"]),
		Case("escaped quotes inside quotes", @"~\~in\~~", ["~in~"]),
		Case("an option followed by a quoted value", "--note ~needs a test~", ["--note", "needs a test"]),
		Case("two quoted arguments", "~a b~ ~c d~", ["a b", "c d"]),
	];

	[Theory]
	[MemberData(nameof(Cases))]
	public void Split_FollowsTheWindowsCommandLineRules(string arguments, string[] expected)
	{
		Assert.Equal(expected, CommandLine.Split(arguments));
	}

	private static TheoryDataRow<string, string[]> Case(string description, string arguments, string[] expected)
	{
		return new TheoryDataRow<string, string[]>(Quoted(arguments), [.. expected.Select(Quoted)]) { TestDisplayName = description };
	}

	private static string Quoted(string text)
	{
		return text.Replace('~', '"');
	}
}
