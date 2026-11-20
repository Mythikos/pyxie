using Microsoft.AspNetCore.Components;

using Pyxie.Data.Colors;

namespace Pyxie.Web.Components.Categories;

/// <summary>Chooses a category's color: the quick colors as swatches, and any other color from the browser's color picker, behind a fourth swatch that shows the color while it is the one chosen. The picker always holds the chosen color, so it opens on it, and a color it moves to is reported as it moves, so a chip in the color being chosen follows it.</summary>
public partial class CategoryColorPicker : ComponentBase
{
	// How the custom swatch shows the chosen color, or null while a quick color is chosen and the swatch only offers the picker.
	private CategoryChipAppearance? _custom;

	/// <summary>Gets or sets the chosen color.</summary>
	[Parameter]
	[EditorRequired]
	public required HexColor Value { get; set; }

	/// <summary>Gets or sets the callback that receives a newly chosen color. It is not raised for the color already chosen, in whatever case the browser reports it.</summary>
	[Parameter]
	public EventCallback<HexColor> ValueChanged { get; set; }

	/// <summary>Gets or sets what the color is for, such as a category's name, which ends every accessible name in the choice so two choices on a page are told apart; or <see langword="null"/> for a category being added.</summary>
	[Parameter]
	public string? Subject { get; set; }

	/// <summary>Gets or sets a value indicating whether the choice is disabled.</summary>
	[Parameter]
	public bool Disabled { get; set; }

	// The radios' group name too, so the swatches of two choices on a page are never one group.
	private string GroupLabel => Subject is null ? "New category color" : $"Color for {Subject}";

	// A color input reports its value in lower case, so it is given the chosen color that way too.
	private string WellValue => Value.ToString().ToLowerInvariant();

	private string CustomClass => _custom is null ? "category-swatch-custom" : "category-swatch-custom category-swatch-custom-chosen";

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		_custom = CategoryPreset.Find(Value) is null ? CategoryChipAppearance.Of(Value) : null;
	}

	private string OptionLabel(string option)
	{
		return Subject is null ? option : $"{option} for {Subject}";
	}

	private Task PickAsync(string text)
	{
		return HexColor.TryParse(text, out HexColor color) ? ChooseAsync(color) : Task.CompletedTask;
	}

	private Task ChooseAsync(HexColor color)
	{
		return color == Value ? Task.CompletedTask : ValueChanged.InvokeAsync(color);
	}
}
