namespace Pyxie.Web.Components.Repository.Diffs;

/// <summary>Classifies the lines of a unified diff as git prints it for a commit. A line's first character is not enough: inside a hunk a removed line whose text starts with <c>-- </c> reads <c>--- </c>, exactly like a file header. So the parser follows the structure instead: everything from a <c>diff </c> line to the next hunk header is header, and everything from a hunk header to the next <c>diff </c> line is that hunk's body, whose lines are context, removed, or added by their first character. A line of a body always starts with a space, <c>+</c>, <c>-</c>, or <c>\</c>, so a bare <c>diff </c> or <c>@@</c> can only be the next file or hunk.</summary>
public static class DiffParser
{
	private const string DiffStart = "diff ";
	private const string HunkStart = "@@";
	private const string TruncationStart = "[truncated: ";
	private const string TruncationEnd = " characters omitted]";

	/// <summary>Splits a diff into blocks of consecutive lines of one kind. Each file's header starts a new block.</summary>
	/// <param name="patch">The diff. A cut diff may carry the <c>[truncated: N characters omitted]</c> marker the process runner leaves, possibly glued to the end of a half line.</param>
	/// <returns>The blocks in order; empty for an empty diff.</returns>
	public static IReadOnlyList<DiffBlock> Parse(string patch)
	{
		DiffBlockBuilder blocks = new();
		string body = patch.EndsWith('\n') ? patch[..^1] : patch;
		bool inHeader = false;

		if (body.Length == 0)
		{
			return blocks.Build();
		}

		foreach (string rawLine in body.Split('\n'))
		{
			string line = rawLine.TrimEnd('\r');
			int marker = FindTruncationMarker(line);

			if (marker < 0)
			{
				blocks.Add(Classify(line, ref inHeader), line, line.StartsWith(DiffStart, StringComparison.Ordinal));
				continue;
			}

			if (marker > 0)
			{
				string before = line[..marker];
				blocks.Add(Classify(before, ref inHeader), before, before.StartsWith(DiffStart, StringComparison.Ordinal));
			}

			// What follows a cut resumes somewhere inside the diff, so it is read by its first character until the next header or hunk gives the parser its bearings again.
			blocks.Add(DiffBlockKind.Note, line[marker..], startsFile: false);
			inHeader = false;
		}

		return blocks.Build();
	}

	private static DiffBlockKind Classify(string line, ref bool inHeader)
	{
		if (line.StartsWith(DiffStart, StringComparison.Ordinal))
		{
			inHeader = true;

			return DiffBlockKind.Header;
		}

		if (line.StartsWith(HunkStart, StringComparison.Ordinal))
		{
			inHeader = false;

			return DiffBlockKind.Hunk;
		}

		if (inHeader)
		{
			return DiffBlockKind.Header;
		}

		if (line.Length == 0)
		{
			return DiffBlockKind.Context;
		}

		return line[0] switch
		{
			'\\' => DiffBlockKind.Note,
			'+' => DiffBlockKind.Added,
			'-' => DiffBlockKind.Removed,
			_ => DiffBlockKind.Context,
		};
	}

	// The runner glues the marker to whatever the kept beginning ended with, so it can end a line that has other text before it.
	private static int FindTruncationMarker(string line)
	{
		if (!line.EndsWith(TruncationEnd, StringComparison.Ordinal))
		{
			return -1;
		}

		int start = line.LastIndexOf(TruncationStart, StringComparison.Ordinal);
		int digitsStart = start + TruncationStart.Length;
		int digitsEnd = line.Length - TruncationEnd.Length;

		return start >= 0 && digitsEnd > digitsStart && line[digitsStart..digitsEnd].All(char.IsAsciiDigit) ? start : -1;
	}
}
