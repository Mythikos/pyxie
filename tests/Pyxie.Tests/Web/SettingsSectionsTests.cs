using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class SettingsSectionsTests
{
	[Fact]
	public void All_ListsTheSectionsInTheOrderThePageShowsThem()
	{
		Assert.Equal([("agents", "Agents"), ("gates", "Gates"), ("categories", "Categories"), ("sandbox", "Sandbox"), ("global", "Global")], SettingsSections.All.Select(section => (section.Id, section.Title)));
	}

	[Theory]
	[InlineData("http://localhost/settings#global", "global")]
	[InlineData("http://localhost/settings#agents", "agents")]
	[InlineData("http://localhost/settings#gates", "gates")]
	[InlineData("http://localhost/settings#categories", "categories")]
	[InlineData("http://localhost/settings#sandbox", "sandbox")]
	[InlineData("http://localhost/settings#SANDBOX", "sandbox")]
	[InlineData("http://localhost/settings#%67ates", "gates")]
	public void FromUri_FindsTheSectionTheFragmentNames(string uri, string expectedId)
	{
		Assert.Equal(expectedId, SettingsSections.FromUri(uri).Id);
	}

	[Theory]
	[InlineData("http://localhost/settings")]
	[InlineData("http://localhost/settings#")]
	[InlineData("http://localhost/settings#nowhere")]
	[InlineData("http://localhost/settings#agents-heading")]
	[InlineData("http://localhost/")]
	public void FromUri_StartsAtAgentsWhenThereIsNoFragmentOrItNamesNoSection(string uri)
	{
		Assert.Equal("agents", SettingsSections.FromUri(uri).Id);
	}
}
