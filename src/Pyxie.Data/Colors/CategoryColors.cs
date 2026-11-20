namespace Pyxie.Data.Colors;

/// <summary>Owns the quick colors a category can be given in one press. They are the design's chip tints, <c>--pyx-lane</c>, <c>--pyx-success-tint</c>, and <c>--pyx-pink-tint</c> in app.css, so a category in one of them is drawn with the design's own text color for that tint.</summary>
public static class CategoryColors
{
	/// <summary>Gets the plain chip's tint, which a category has unless a person chose another color.</summary>
	public static HexColor Neutral { get; } = new(0xED, 0xED, 0xE9);

	/// <summary>Gets the green tint.</summary>
	public static HexColor Green { get; } = new(0xEA, 0xF3, 0xE2);

	/// <summary>Gets the pink tint.</summary>
	public static HexColor Pink { get; } = new(0xFB, 0xE9, 0xEC);
}
