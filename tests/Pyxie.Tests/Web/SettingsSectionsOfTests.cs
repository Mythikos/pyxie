using Pyxie.Data.Transfer;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>An import names each part of the settings a file carries as the Settings page names its section, so every part has a section of its own.</summary>
public sealed class SettingsSectionsOfTests
{
	[Theory]
	[InlineData(TransferSection.Agents, "agents", "Agents")]
	[InlineData(TransferSection.Gates, "gates", "Gates")]
	[InlineData(TransferSection.Categories, "categories", "Categories")]
	[InlineData(TransferSection.Sandbox, "sandbox", "Sandbox")]
	public void Of_GivesThePagesSectionThatShowsThePart(TransferSection section, string expectedId, string expectedTitle)
	{
		SettingsSection shown = SettingsSections.Of(section);

		Assert.Equal((expectedId, expectedTitle), (shown.Id, shown.Title));
		Assert.Same(Assert.Single(SettingsSections.All, candidate => candidate.Id == expectedId), shown);
	}

	[Fact]
	public void Of_EveryPart_GivesEachOfThePagesSectionsOnceInThePagesOrder()
	{
		Assert.Equal(SettingsSections.ProjectSections, Enum.GetValues<TransferSection>().Select(SettingsSections.Of));
	}
}
