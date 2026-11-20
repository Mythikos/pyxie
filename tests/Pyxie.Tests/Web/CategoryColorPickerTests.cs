using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Web.Components.Categories;
using Pyxie.Web.Components.Categories.Colors;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The color choice offers the quick colors as radios and any other color through the browser's picker, which always holds the chosen color. It reports a newly chosen color, never the one already chosen, in whatever case the browser writes it, and names its controls after what the color is for.</summary>
public sealed class CategoryColorPickerTests
{
	[Fact]
	public async Task Render_ForANewCategory_NamesTheChoiceAndItsOptionsForIt()
	{
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: null, []));

		GateRenderedElement group = Assert.Single(await host.FindElementsAsync("div", div => div.Attribute("role") == "group"));
		Assert.Equal("New category color", group.Attribute("aria-label"));
		IReadOnlyList<GateRenderedElement> radios = await RadiosAsync(host);
		Assert.Equal(["Neutral", "Green", "Pink"], radios.Select(radio => radio.Attribute("aria-label")));
		Assert.All(radios, radio => Assert.Equal("New category color", radio.Attribute("name")));
		Assert.Equal("Custom color", (await WellAsync(host)).Attribute("aria-label"));
	}

	[Fact]
	public async Task Render_ForACategory_EndsEveryNameWithTheCategory()
	{
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: "Bug", []));

		GateRenderedElement group = Assert.Single(await host.FindElementsAsync("div", div => div.Attribute("role") == "group"));
		Assert.Equal("Color for Bug", group.Attribute("aria-label"));
		IReadOnlyList<GateRenderedElement> radios = await RadiosAsync(host);
		Assert.Equal(["Neutral for Bug", "Green for Bug", "Pink for Bug"], radios.Select(radio => radio.Attribute("aria-label")));
		Assert.All(radios, radio => Assert.Equal("Color for Bug", radio.Attribute("name")));
		Assert.Equal("Custom color for Bug", (await WellAsync(host)).Attribute("aria-label"));
	}

	[Fact]
	public async Task Render_OfAQuickColor_ChecksItAndOffersThePickerOnTheFourthSwatch()
	{
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Pink, subject: null, []));

		Assert.Equal([null, null, "true"], (await RadiosAsync(host)).Select(radio => radio.Attribute("checked")));
		Assert.Equal("#fbe9ec", (await WellAsync(host)).Attribute("value"));
		GateRenderedElement swatch = await CustomSwatchAsync(host);
		Assert.Equal("category-swatch-custom", swatch.Attribute("class"));
		Assert.Null(swatch.Attribute("style"));
	}

	[Fact]
	public async Task Render_OfAnyOtherColor_ChecksNoQuickColorAndShowsTheColorOnTheFourthSwatch()
	{
		HexColor color = HexColor.Parse("#1F3A93");
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(color, subject: null, []));

		Assert.All(await RadiosAsync(host), radio => Assert.Null(radio.Attribute("checked")));
		Assert.Equal("#1f3a93", (await WellAsync(host)).Attribute("value"));
		GateRenderedElement swatch = await CustomSwatchAsync(host);
		Assert.Equal("category-swatch-custom category-swatch-custom-chosen", swatch.Attribute("class"));
		Assert.Equal($"--chip-tint: #1F3A93; --chip-ink: {ChipInk.For(color)}", swatch.Attribute("style"));
	}

	[Fact]
	public async Task ChoosingAQuickColor_ReportsIt()
	{
		List<HexColor> chosen = [];
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: null, chosen));

		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);

		Assert.Equal([CategoryColors.Green], chosen);
	}

	[Fact]
	public async Task ChoosingTheQuickColorAlreadyChosen_ReportsNothing()
	{
		List<HexColor> chosen = [];
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Green, subject: null, chosen));

		await host.SetCheckedAsync(await host.FindRadioAsync("Green"), true);

		Assert.Empty(chosen);
	}

	[Theory]
	[InlineData("#1f3a93")]
	[InlineData("#1F3A93")]
	public async Task PickingAColor_ReportsIt(string reported)
	{
		List<HexColor> chosen = [];
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: null, chosen));

		await host.TypeAsync(await WellAsync(host), reported);

		Assert.Equal([new HexColor(0x1F, 0x3A, 0x93)], chosen);
	}

	[Fact]
	public async Task ThePicker_ReportsAColorAsItMovesNotOnlyWhenItCloses()
	{
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: null, []));

		GateRenderedElement well = await WellAsync(host);

		Assert.True(well.EventHandlers.ContainsKey("oninput"), "The color input reports each color the browser's picker moves to, so a chip in the color follows it.");
		Assert.False(well.EventHandlers.ContainsKey("onchange"), "The color input reports on input only, once per color.");
	}

	[Theory]
	[InlineData("#eaf3e2")]
	[InlineData("#EAF3E2")]
	public async Task PickingTheColorAlreadyChosen_InEitherCase_ReportsNothing(string reported)
	{
		List<HexColor> chosen = [];
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Green, subject: null, chosen));

		await host.TypeAsync(await WellAsync(host), reported);

		Assert.Empty(chosen);
	}

	[Theory]
	[InlineData("")]
	[InlineData("red")]
	[InlineData("#1F3A93;color:red")]
	public async Task PickingSomethingThatIsNotAColor_ReportsNothing(string reported)
	{
		List<HexColor> chosen = [];
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryColorPicker>(Parameters(CategoryColors.Neutral, subject: null, chosen));

		await host.TypeAsync(await WellAsync(host), reported);

		Assert.Empty(chosen);
	}

	[Fact]
	public async Task Disabled_DisablesEveryOption()
	{
		await using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
		await using GateInteractiveHost host = new(services);
		Dictionary<string, object?> parameters = Parameters(CategoryColors.Neutral, subject: null, []);
		parameters[nameof(CategoryColorPicker.Disabled)] = true;
		await host.StartAsync<CategoryColorPicker>(parameters);

		Assert.All(await host.FindElementsAsync("input"), input => Assert.Equal("true", input.Attribute("disabled")));
	}

	private static Dictionary<string, object?> Parameters(HexColor value, string? subject, List<HexColor> chosen)
	{
		return new Dictionary<string, object?>
		{
			[nameof(CategoryColorPicker.Value)] = value,
			[nameof(CategoryColorPicker.ValueChanged)] = EventCallback.Factory.Create<HexColor>(new object(), chosen.Add),
			[nameof(CategoryColorPicker.Subject)] = subject,
		};
	}

	private static Task<IReadOnlyList<GateRenderedElement>> RadiosAsync(GateInteractiveHost host)
	{
		return host.FindElementsAsync("input", input => input.Attribute("type") == "radio");
	}

	private static async Task<GateRenderedElement> WellAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("type") == "color"));
	}

	private static async Task<GateRenderedElement> CustomSwatchAsync(GateInteractiveHost host)
	{
		return Assert.Single(await host.FindElementsAsync("span", span => (span.Attribute("class") ?? string.Empty).StartsWith("category-swatch-custom", StringComparison.Ordinal)));
	}
}
