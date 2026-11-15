namespace Pyxie.Tests.Support;

/// <summary>Finds component elements in Razor markup without a Razor compiler dependency.</summary>
/// <remarks>An element is a component candidate when its name starts with an uppercase letter. C# generic arguments such as <c>List&lt;Agent&gt;</c> are reported too; callers resolve each name and ignore those that are not components.</remarks>
public static class RazorMarkupScanner
{
	private static readonly (string Open, string Close)[] s_comments = [("@*", "*@"), ("<!--", "-->")];

	/// <summary>Finds every component element and the attribute names written on it.</summary>
	/// <param name="markup">The Razor markup.</param>
	/// <returns>The elements in source order.</returns>
	public static IReadOnlyList<RazorComponentUsage> FindComponents(string markup)
	{
		List<RazorComponentUsage> usages = [];
		int index = 0;

		while (index < markup.Length)
		{
			if (TrySkipComment(markup, ref index))
			{
				continue;
			}

			if (markup[index] == '<' && index + 1 < markup.Length && char.IsAsciiLetterUpper(markup[index + 1]))
			{
				usages.Add(ReadElement(markup, ref index));
				continue;
			}

			index++;
		}

		return usages;
	}

	private static bool TrySkipComment(string markup, ref int index)
	{
		foreach ((string open, string close) in s_comments)
		{
			if (string.CompareOrdinal(markup, index, open, 0, open.Length) != 0)
			{
				continue;
			}

			int end = markup.IndexOf(close, index + open.Length, StringComparison.Ordinal);
			index = end < 0 ? markup.Length : end + close.Length;

			return true;
		}

		return false;
	}

	private static RazorComponentUsage ReadElement(string markup, ref int index)
	{
		int line = 1 + markup.AsSpan(0, index).Count('\n');
		int nameStart = ++index;

		while (index < markup.Length && (char.IsAsciiLetterOrDigit(markup[index]) || markup[index] is '_' or '.'))
		{
			index++;
		}

		string name = markup[nameStart..index];
		List<string> attributes = [];

		while (index < markup.Length)
		{
			SkipWhitespace(markup, ref index);

			if (index >= markup.Length)
			{
				break;
			}

			if (markup[index] == '>')
			{
				index++;
				break;
			}

			if (IsSelfClosingEnd(markup, index))
			{
				index += 2;
				break;
			}

			string attribute = ReadAttributeName(markup, ref index);
			if (attribute.Length == 0)
			{
				index++;
				continue;
			}

			attributes.Add(attribute);
			SkipWhitespace(markup, ref index);

			if (index < markup.Length && markup[index] == '=')
			{
				index++;
				SkipWhitespace(markup, ref index);
				SkipValue(markup, ref index);
			}
		}

		return new RazorComponentUsage(name, line, attributes);
	}

	private static string ReadAttributeName(string markup, ref int index)
	{
		int start = index;

		while (index < markup.Length && !char.IsWhiteSpace(markup[index]) && markup[index] is not ('=' or '>') && !IsSelfClosingEnd(markup, index))
		{
			index++;
		}

		return markup[start..index];
	}

	private static bool IsSelfClosingEnd(string markup, int index) => markup[index] == '/' && index + 1 < markup.Length && markup[index + 1] == '>';

	private static void SkipWhitespace(string markup, ref int index)
	{
		while (index < markup.Length && char.IsWhiteSpace(markup[index]))
		{
			index++;
		}
	}

	private static void SkipValue(string markup, ref int index)
	{
		if (index >= markup.Length)
		{
			return;
		}

		if (markup[index] == '"')
		{
			SkipQuotedValue(markup, ref index);
		}
		else if (markup[index] == '\'')
		{
			int end = markup.IndexOf('\'', index + 1);
			index = end < 0 ? markup.Length : end + 1;
		}
		else
		{
			while (index < markup.Length && !char.IsWhiteSpace(markup[index]) && markup[index] != '>')
			{
				index++;
			}
		}
	}

	// A double-quoted value ends at the first quote outside a parenthesized C# expression, so @("text") and @(() => "x") stay intact.
	private static void SkipQuotedValue(string markup, ref int index)
	{
		int depth = 0;
		index++;

		while (index < markup.Length)
		{
			char current = markup[index++];

			if (current == '"' && depth == 0)
			{
				return;
			}

			if (current == '"')
			{
				SkipCSharpString(markup, ref index);
			}
			else if (current == '(')
			{
				depth++;
			}
			else if (current == ')' && depth > 0)
			{
				depth--;
			}
		}
	}

	private static void SkipCSharpString(string markup, ref int index)
	{
		while (index < markup.Length)
		{
			char current = markup[index++];

			if (current == '\\')
			{
				index++;
			}
			else if (current == '"')
			{
				return;
			}
		}
	}
}
