using Pyxie.Data.Colors;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Contrast is the WCAG 2 ratio, checked against values the guidelines and the design publish, so other tests can measure inks with it.</summary>
public sealed class ContrastRatioTests
{
	[Theory]
	[InlineData("#000000", "#FFFFFF", 21.0)]
	[InlineData("#FFFFFF", "#000000", 21.0)]
	[InlineData("#767676", "#FFFFFF", 4.54)]
	[InlineData("#777777", "#FFFFFF", 4.48)]
	[InlineData("#4A4C53", "#EDEDE9", 7.30)]
	[InlineData("#2F5C29", "#EAF3E2", 6.86)]
	[InlineData("#8E2F42", "#FBE9EC", 6.82)]
	[InlineData("#3D7336", "#FFFFFF", 5.67)]
	[InlineData("#EAF3E2", "#EAF3E2", 1.0)]
	public void Between_IsTheWcagRatioWhicheverColorIsLighter(string first, string second, double expected)
	{
		Assert.Equal(expected, ContrastRatio.Between(HexColor.Parse(first), HexColor.Parse(second)), 2);
	}

	[Theory]
	[InlineData("#000000", 0.0)]
	[InlineData("#FFFFFF", 1.0)]
	[InlineData("#808080", 0.2159)]
	[InlineData("#FF0000", 0.2126)]
	[InlineData("#00FF00", 0.7152)]
	[InlineData("#0000FF", 0.0722)]
	public void RelativeLuminance_IsTheWcagLuminance(string color, double expected)
	{
		Assert.Equal(expected, ContrastRatio.RelativeLuminance(HexColor.Parse(color)), 4);
	}
}
