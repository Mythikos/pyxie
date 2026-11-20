using Microsoft.AspNetCore.Components;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;

namespace Pyxie.Web.Components.Categories;

/// <summary>A category's name as a chip in the category's color, as cards show it.</summary>
public partial class CategoryChip : ComponentBase
{
	private CategoryChipAppearance _appearance = CategoryChipAppearance.Of(CategoryColors.Neutral);

	/// <summary>Gets or sets the category.</summary>
	[Parameter]
	[EditorRequired]
	public required Category Category { get; set; }

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		_appearance = CategoryChipAppearance.Of(Category.Color);
	}
}
