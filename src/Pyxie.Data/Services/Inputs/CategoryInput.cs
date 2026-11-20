using Pyxie.Data.Colors;
using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The editable fields of a category.</summary>
/// <param name="Name">The unique, case-insensitive name.</param>
/// <param name="Color">The color of the category's chip.</param>
public sealed record CategoryInput(string Name, HexColor Color)
{
	/// <summary>Gets the fields a category has now.</summary>
	/// <param name="category">The category.</param>
	/// <returns>The category's fields.</returns>
	public static CategoryInput From(Category category)
	{
		return new CategoryInput(category.Name, category.Color);
	}
}
