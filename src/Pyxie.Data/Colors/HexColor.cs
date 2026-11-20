using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Pyxie.Data.Colors;

/// <summary>An opaque sRGB color with eight bits per channel, written <c>#RRGGBB</c>. Every value is a color a browser can show, so a color that reached this type, such as a category's, needs no checking again: it is parsed once, where it enters, and written the one way <see cref="ToString"/> writes it.</summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
public readonly record struct HexColor(byte Red, byte Green, byte Blue)
{
	/// <summary>The length of a color written <c>#RRGGBB</c>.</summary>
	public const int Length = 7;

	/// <summary>Reads a color written <c>#RRGGBB</c>, as a browser's color input reports it.</summary>
	/// <param name="text">The text: <c>#</c> and six ASCII hex digits in either case, with nothing before or after.</param>
	/// <param name="color">The color read, or black when the text is not a color.</param>
	/// <returns><see langword="true"/> when the text is a color written <c>#RRGGBB</c>.</returns>
	public static bool TryParse([NotNullWhen(true)] string? text, out HexColor color)
	{
		color = default;
		if (text is not { Length: Length } || text[0] != '#')
		{
			return false;
		}

		ReadOnlySpan<char> digits = text.AsSpan(1);
		foreach (char digit in digits)
		{
			if (!char.IsAsciiHexDigit(digit))
			{
				return false;
			}
		}

		color = new HexColor(ReadChannel(digits[..2]), ReadChannel(digits[2..4]), ReadChannel(digits[4..]));

		return true;
	}

	/// <summary>Reads a color written <c>#RRGGBB</c> that is known to be one, such as a stored category color.</summary>
	/// <param name="text">The text: <c>#</c> and six ASCII hex digits in either case.</param>
	/// <returns>The color.</returns>
	/// <exception cref="FormatException">The text is not a color written <c>#RRGGBB</c>.</exception>
	public static HexColor Parse(string text)
	{
		return TryParse(text, out HexColor color) ? color : throw new FormatException($"'{text}' is not a color written #RRGGBB.");
	}

	/// <summary>Writes the color as <c>#RRGGBB</c> in upper case, the one way colors are stored and given to CSS.</summary>
	/// <returns>The color, such as <c>#EAF3E2</c>.</returns>
	public override string ToString()
	{
		return string.Create(CultureInfo.InvariantCulture, $"#{Red:X2}{Green:X2}{Blue:X2}");
	}

	private static byte ReadChannel(ReadOnlySpan<char> digits)
	{
		return byte.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	}
}
