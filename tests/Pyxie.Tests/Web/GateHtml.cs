using System.Net;
using System.Text.RegularExpressions;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Reads the static HTML a gates component renders, so a test can ask about one element instead of searching the whole page text.</summary>
public static class GateHtml
{
	/// <summary>Finds the first opening tag of an element that contains a fragment, such as <c>aria-label="Move Build up"</c>.</summary>
	/// <param name="html">The rendered HTML.</param>
	/// <param name="tagName">The element name, such as <c>button</c>.</param>
	/// <param name="fragment">Text the opening tag must contain.</param>
	/// <returns>The opening tag, from <c>&lt;</c> to <c>&gt;</c>.</returns>
	public static string OpenTag(string html, string tagName, string fragment)
	{
		Match match = Regex.Match(html, $@"<{tagName}\b[^>]*{Regex.Escape(fragment)}[^>]*>");
		Assert.True(match.Success, $"The rendered HTML has no <{tagName}> containing {fragment}.");

		return match.Value;
	}

	/// <summary>Determines whether an opening tag carries an attribute, with or without a value.</summary>
	/// <param name="tag">The opening tag.</param>
	/// <param name="attribute">The attribute name.</param>
	/// <returns><see langword="true"/> when the attribute is present.</returns>
	public static bool HasAttribute(string tag, string attribute)
	{
		return Regex.IsMatch(tag, $@"\s{Regex.Escape(attribute)}(?=[\s/>=])");
	}

	/// <summary>Reads an attribute value from an opening tag.</summary>
	/// <param name="tag">The opening tag.</param>
	/// <param name="attribute">The attribute name.</param>
	/// <returns>The decoded value, or <see langword="null"/> when the tag has no such attribute with a value.</returns>
	public static string? AttributeValue(string tag, string attribute)
	{
		Match match = Regex.Match(tag, $@"\s{Regex.Escape(attribute)}=""([^""]*)""");

		return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
	}

	/// <summary>Reads the text of every element that has a class, in document order. The element must contain only text.</summary>
	/// <param name="html">The rendered HTML.</param>
	/// <param name="cssClass">The class the element was given in markup.</param>
	/// <returns>The decoded text of each element.</returns>
	public static IReadOnlyList<string> TextOfElementsWithClass(string html, string cssClass)
	{
		return Regex.Matches(html, $@"class=""{Regex.Escape(cssClass)}""[^>]*>([^<]*)<")
			.Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))
			.ToList();
	}

	/// <summary>Counts the buttons that show exactly a label.</summary>
	/// <param name="html">The rendered HTML.</param>
	/// <param name="label">The button text.</param>
	/// <returns>The number of buttons.</returns>
	public static int CountButtons(string html, string label)
	{
		return Regex.Matches(html, $@"<button\b[^>]*>{Regex.Escape(label)}</button>").Count;
	}

	/// <summary>Counts the buttons that show a label, with or without an icon in front of it.</summary>
	/// <param name="html">The rendered HTML.</param>
	/// <param name="label">The text after the icon.</param>
	/// <returns>The number of buttons.</returns>
	public static int CountIconButtons(string html, string label)
	{
		return Regex.Matches(html, $@"<button\b[^>]*>(?:<span class=""anticon[^""]*"">.*?</span>)?{Regex.Escape(label)}</button>", RegexOptions.Singleline).Count;
	}
}
