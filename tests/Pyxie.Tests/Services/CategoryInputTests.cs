using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="CategoryInput.From"/> gives a category's name and color, the fields a person sets on it.</summary>
public sealed class CategoryInputTests
{
	[Theory]
	[InlineData("Research", "#1F3A93")]
	[InlineData("Bug", "#FBE9EC")]
	[InlineData("Chore", "#000000")]
	public void From_GivesTheNameAndColorOfTheCategory(string name, string color)
	{
		Category category = new() { Id = 4, Name = name, Color = HexColor.Parse(color) };

		CategoryInput input = CategoryInput.From(category);

		Assert.Equal(new CategoryInput(name, HexColor.Parse(color)), input);
	}
}
