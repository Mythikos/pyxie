namespace Pyxie.Runtime.Workspaces;

/// <summary>Recognizes the conflict markers git writes into a file it could not merge, so a resolution that still has some can be refused. A marker is a line that starts with seven <c>&lt;</c>, <c>&gt;</c>, or <c>|</c> characters and then ends or goes on with a space, or a line of exactly seven <c>=</c> characters. Text that merely looks like a marker is allowed when the same line is already in one of the file's versions before the merge: a document about git, test data, or a heading underlined with <c>=======</c>.</summary>
public static class ConflictMarkers
{
	private const int MarkerLength = 7;

	/// <summary>Determines whether a line is a conflict marker.</summary>
	/// <param name="line">The line, with or without its line break.</param>
	/// <returns><see langword="true"/> for a line git writes around the two sides of a conflict: the one that opens the first side, the one that opens the base, the one that separates the sides, and the one that closes the second.</returns>
	public static bool IsMarker(string line)
	{
		string text = line.TrimEnd('\r', '\n');
		if (text.Length < MarkerLength)
		{
			return false;
		}

		char first = text[0];
		if (first is not ('<' or '>' or '|' or '='))
		{
			return false;
		}

		for (int index = 1; index < MarkerLength; index++)
		{
			if (text[index] != first)
			{
				return false;
			}
		}

		return first == '=' ? text.Length == MarkerLength : text.Length == MarkerLength || text[MarkerLength] == ' ';
	}

	/// <summary>Determines whether a resolved file still has conflict markers that its versions before the merge did not have.</summary>
	/// <param name="resolved">The text of the file as the merge commit has it.</param>
	/// <param name="versions">The text of the file in each parent of the merge; <see langword="null"/> for a parent that has no such file, or none that is text.</param>
	/// <returns><see langword="true"/> when a line of <paramref name="resolved"/> is a marker and none of the versions has the same line.</returns>
	public static bool HasUnresolved(string resolved, IEnumerable<string?> versions)
	{
		HashSet<string>? known = null;
		foreach (string line in Lines(resolved))
		{
			if (!IsMarker(line))
			{
				continue;
			}

			known ??= [.. versions.OfType<string>().SelectMany(Lines)];
			if (!known.Contains(line))
			{
				return true;
			}
		}

		return false;
	}

	private static IEnumerable<string> Lines(string text)
	{
		return text.Split('\n').Select(line => line.TrimEnd('\r'));
	}
}
