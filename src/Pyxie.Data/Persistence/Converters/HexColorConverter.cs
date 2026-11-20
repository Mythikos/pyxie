using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using Pyxie.Data.Colors;

namespace Pyxie.Data.Persistence.Converters;

/// <summary>Stores a <see cref="HexColor"/> as the text <see cref="HexColor.ToString"/> writes, <c>#RRGGBB</c> in upper case.</summary>
public sealed class HexColorConverter : ValueConverter<HexColor, string>
{
	/// <summary>Initializes a new instance of the <see cref="HexColorConverter"/> class.</summary>
	public HexColorConverter()
		: base(color => color.ToString(), text => HexColor.Parse(text))
	{
	}
}
