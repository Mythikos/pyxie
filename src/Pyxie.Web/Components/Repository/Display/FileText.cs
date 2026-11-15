using System.Globalization;
using System.Text;

namespace Pyxie.Web.Components.Repository.Display;

/// <summary>Measures a file's text for the viewer, which shows the numbers of its lines beside it.</summary>
public static class FileText
{
	/// <summary>Counts the lines of a text. A final line break ends the last line instead of starting another one.</summary>
	/// <param name="text">The text.</param>
	/// <returns>0 for an empty text, otherwise the number of lines.</returns>
	public static int LineCount(string text)
	{
		if (text.Length == 0)
		{
			return 0;
		}

		int breaks = text.Count(character => character == '\n');

		return text[^1] == '\n' ? breaks : breaks + 1;
	}

	/// <summary>Writes the line numbers one per line, to be shown as a single block next to the text, so a long file costs one element and not one per line.</summary>
	/// <param name="lineCount">The number of lines.</param>
	/// <returns>The numbers from 1 up, separated by line breaks.</returns>
	public static string Gutter(int lineCount)
	{
		StringBuilder numbers = new();

		for (int line = 1; line <= lineCount; line++)
		{
			if (line > 1)
			{
				numbers.Append('\n');
			}

			numbers.Append(line.ToString(CultureInfo.InvariantCulture));
		}

		return numbers.ToString();
	}
}
