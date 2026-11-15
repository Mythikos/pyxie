using Pyxie.Data.Enums;

namespace Pyxie.Web.Components.Categories;

/// <summary>Owns how a category's color looks: the shared chip classes in app.css for each <see cref="CategoryColor"/>.</summary>
public static class CategoryChipClasses
{
	/// <summary>Gets the CSS classes of a chip in a category's color.</summary>
	/// <param name="color">The category's color.</param>
	/// <returns>The classes, such as <c>pyx-chip pyx-chip-green</c>; the neutral chip for <see cref="CategoryColor.Neutral"/> and for any value that is not defined.</returns>
	public static string Of(CategoryColor color)
	{
		return color switch
		{
			CategoryColor.Green => "pyx-chip pyx-chip-green",
			CategoryColor.Pink => "pyx-chip pyx-chip-pink",
			_ => "pyx-chip",
		};
	}
}
