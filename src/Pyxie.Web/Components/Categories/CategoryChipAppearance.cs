using Pyxie.Data.Colors;

using Pyxie.Web.Components.Categories.Colors;

namespace Pyxie.Web.Components.Categories;

/// <summary>Owns how a chip in a category's color looks. A quick color is drawn with its shared classes in app.css, so the design's tints keep the design's own text colors. Any other color is drawn with <c>pyx-chip-custom</c>, which reads the tint and its derived text color (<see cref="ChipInk"/>) from the custom properties <c>--chip-tint</c> and <c>--chip-ink</c> that <see cref="Style"/> sets on the element.</summary>
/// <param name="Classes">The CSS classes of the chip, such as <c>pyx-chip pyx-chip-green</c>.</param>
/// <param name="Style">The inline style that sets the chip's colors, or <see langword="null"/> for a quick color, whose classes carry them.</param>
public sealed record CategoryChipAppearance(string Classes, string? Style)
{
	/// <summary>Gets how a chip in a color looks.</summary>
	/// <param name="color">The category's color.</param>
	/// <returns>The chip's classes, and for a color that is not a quick color, its style.</returns>
	public static CategoryChipAppearance Of(HexColor color)
	{
		return CategoryPreset.Find(color) is CategoryPreset preset ? new CategoryChipAppearance(preset.ChipClasses, null) : new CategoryChipAppearance("pyx-chip pyx-chip-custom", $"--chip-tint: {color}; --chip-ink: {ChipInk.For(color)}");
	}
}
