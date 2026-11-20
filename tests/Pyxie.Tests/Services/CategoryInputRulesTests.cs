using Pyxie.Data.Colors;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary><see cref="CategoryInputRules"/>: a category's name is required and within the limit once trimmed, and is stored trimmed; every color is a valid one, so the color has no rules and is kept as it is.</summary>
public sealed class CategoryInputRulesTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateName_OfAMissingOrBlankName_SaysTheNameIsRequired(string? name)
	{
		Assert.Equal("Category name is required.", CategoryInputRules.ValidateName(name));
	}

	[Fact]
	public void ValidateName_OfANameAtTheLimitAfterTrimming_FindsNothing_AndOfOneOver_SaysHowLongItMayBe()
	{
		Assert.Null(CategoryInputRules.ValidateName($"  {new string('n', PyxieLimits.MaxNameLength)}  "));
		Assert.Equal("Category name must be 100 characters or fewer.", CategoryInputRules.ValidateName(new string('n', PyxieLimits.MaxNameLength + 1)));
	}

	[Theory]
	[InlineData("Research")]
	[InlineData("Tech debt")]
	[InlineData("Idée")]
	public void ValidateName_OfAnOrdinaryName_FindsNothing(string name)
	{
		Assert.Null(CategoryInputRules.ValidateName(name));
	}

	[Theory]
	[InlineData("  Tech  debt ", "Tech  debt")]
	[InlineData("\tResearch\r\n", "Research")]
	[InlineData("Bug", "Bug")]
	[InlineData("   ", "")]
	public void Normalize_TrimsTheName_AndKeepsTheColor(string name, string expected)
	{
		HexColor color = new(0x1F, 0x3A, 0x93);

		Assert.Equal(new CategoryInput(expected, color), CategoryInputRules.Normalize(new CategoryInput(name, color)));
	}
}
