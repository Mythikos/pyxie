using Pyxie.Data.Enums;

namespace Pyxie.Data.Entities;

/// <summary>A user-editable classification for cards, such as Feature or Bug.</summary>
public class Category
{
	/// <summary>Gets or sets the database identifier.</summary>
	public int Id { get; set; }

	/// <summary>Gets or sets the unique, case-insensitive display name.</summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>Gets or sets the tint the category's chip is shown in. <see cref="CategoryColor.Neutral"/> unless a person chose another.</summary>
	public CategoryColor Color { get; set; }
}
