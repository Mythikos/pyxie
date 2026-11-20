using Pyxie.Data.Colors;

namespace Pyxie.Data.Entities;

/// <summary>A user-editable classification for cards, such as Feature or Bug.</summary>
public class Category
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the unique, case-insensitive display name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the color of the category's chip: its background, any color at all. <see cref="CategoryColors.Neutral"/> unless a person chose another.</summary>
	public HexColor Color { get; set; } = CategoryColors.Neutral;
}
