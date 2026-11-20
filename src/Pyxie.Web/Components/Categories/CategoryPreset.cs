using Pyxie.Data.Colors;

namespace Pyxie.Web.Components.Categories;

/// <summary>A quick color a category can be given in one press, with how the design draws a chip in it: the shared chip classes in app.css, which pair the tint with the design's own text color for it.</summary>
/// <param name="Name">The color's name, as the color choice offers it.</param>
/// <param name="Color">The color.</param>
/// <param name="ChipClasses">The CSS classes of a chip in the color.</param>
public sealed record CategoryPreset(string Name, HexColor Color, string ChipClasses)
{
	/// <summary>Gets the plain chip, which a category has unless a person chose another color.</summary>
	public static CategoryPreset Neutral { get; } = new("Neutral", CategoryColors.Neutral, "pyx-chip");

	/// <summary>Gets the green chip.</summary>
	public static CategoryPreset Green { get; } = new("Green", CategoryColors.Green, "pyx-chip pyx-chip-green");

	/// <summary>Gets the pink chip.</summary>
	public static CategoryPreset Pink { get; } = new("Pink", CategoryColors.Pink, "pyx-chip pyx-chip-pink");

	/// <summary>Gets every quick color, in the order the color choice offers them.</summary>
	public static IReadOnlyList<CategoryPreset> All { get; } = [Neutral, Green, Pink];

	/// <summary>Finds the quick color a color is, if it is one.</summary>
	/// <param name="color">The color.</param>
	/// <returns>The quick color, or <see langword="null"/> for any other color.</returns>
	public static CategoryPreset? Find(HexColor color)
	{
		return All.FirstOrDefault(preset => preset.Color == color);
	}
}
