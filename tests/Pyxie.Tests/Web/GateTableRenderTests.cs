using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GateTableRenderTests
{
	[Fact]
	public async Task ListsTheGatesInTheOrderTheyRunNotTheOrderTheyWereAddedOrAlphabetically()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Lint", 2);
		await GateTestData.AddAtAsync(database, "Tests", 0);
		await GateTestData.AddAtAsync(database, "Build", 1);

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Equal(["Tests", "Build", "Lint"], GateHtml.TextOfElementsWithClass(html, "gate-text"));
	}

	[Fact]
	public async Task ListsTheGatesAsAnOrderedList()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Matches(@"<ol\b[^>]*>\s*<li\b[^>]*>.*?</li>\s*<li\b[^>]*>.*?</li>\s*</ol>", html.Replace("\n", string.Empty, StringComparison.Ordinal));
	}

	[Fact]
	public async Task ShowsOnlyTheHeadingAndAddGateWhenThereAreNoGates()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Matches(@"<h2 id=""gates-heading""[^>]*>Gates</h2>", html);
		Assert.Equal(1, GateHtml.CountIconButtons(html, "Add gate"));
		Assert.DoesNotContain("<li", html, StringComparison.Ordinal);
		Assert.DoesNotContain("No gates", html, StringComparison.Ordinal);
		Assert.DoesNotContain("gate-move-hint", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task IsThePanelTheGatesAnchorPointsAt()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Matches(@"<section id=""gates"" class=""pyx-panel[^""]*"" aria-labelledby=""gates-heading""", html);
	}

	[Fact]
	public async Task ShowsTheCommandAndItsArgumentsTogetherInMonospaceAndTheTimeoutInMinutes()
	{
		await using TestDatabase database = new();
		await CreateAsync(database, GateTestInput.Create("Format", "dotnet", "format --verify-no-changes", 5));

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Equal(["dotnet format --verify-no-changes"], GateHtml.TextOfElementsWithClass(html, "gate-command"));
		Assert.Matches(@"<code class=""gate-command""", html);
		Assert.Matches(@"<span class=""gate-timeout""[^>]*>5 min</span>", html);
	}

	[Fact]
	public async Task ShowsOnlyTheCommandWhenTheGateHasNoArguments()
	{
		await using TestDatabase database = new();
		await CreateAsync(database, GateTestInput.Create("Check", "./check.sh", string.Empty));

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Equal(["./check.sh"], GateHtml.TextOfElementsWithClass(html, "gate-command"));
	}

	[Fact]
	public async Task GivesEveryGateAHandleThatIsAButtonNamedAfterItAndDescribedByHowToMoveIt()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		foreach (string gate in new[] { "Build", "Tests" })
		{
			string handle = GateHtml.OpenTag(html, "button", $"aria-label=\"Move {gate}\"");
			Assert.Equal("true", GateHtml.AttributeValue(handle, "draggable"));
			Assert.Equal("gate-move-hint", GateHtml.AttributeValue(handle, "aria-describedby"));
			Assert.False(GateHtml.HasAttribute(handle, "disabled"), "A handle should stay on the keyboard's tab order, so it is never disabled.");
		}

		Assert.Matches(@"<span id=""gate-move-hint"" class=""pyx-visually-hidden""[^>]*>Use the up and down arrow keys to move\.</span>", html);
	}

	[Fact]
	public async Task TellsTheBrowserItselfToKeepTheArrowKeysOnHandlesAndToAcceptDragsOverGatesAndNothingElse()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		GateRenderedElement list = Assert.Single(await host.FindElementsAsync("ol"));

		string keys = list.Attribute("onkeydown")!;
		Assert.Contains("ArrowUp", keys, StringComparison.Ordinal);
		Assert.Contains("ArrowDown", keys, StringComparison.Ordinal);
		Assert.Contains(".gate-handle", keys, StringComparison.Ordinal);
		Assert.Contains("preventDefault", keys, StringComparison.Ordinal);
		Assert.DoesNotContain("Tab", keys, StringComparison.Ordinal);
		foreach (string drag in new[] { "ondragenter", "ondragover" })
		{
			Assert.Contains(".gate-row", list.Attribute(drag)!, StringComparison.Ordinal);
			Assert.Contains("preventDefault", list.Attribute(drag)!, StringComparison.Ordinal);
		}

		Assert.Empty(list.EventHandlers);
	}

	[Fact]
	public async Task KeepsTheBrowserFromOpeningWhatIsDroppedOnAGate()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		GateRenderedElement row = Assert.Single(await host.FindElementsAsync("li"));

		Assert.Equal("true", row.Attribute("__internal_preventDefault_ondrop"));
		Assert.False((await host.FindButtonAsync("Move Build")).Attributes.ContainsKey("__internal_preventDefault_onkeydown"), "A handle must not swallow Tab and the other keys.");
	}

	[Theory]
	[InlineData("Enabled", "true")]
	[InlineData("Disabled", "false")]
	public async Task EachSwitchShowsWhetherItsGateIsEnabled(string gate, string expectedChecked)
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Enabled", 0, isEnabled: true);
		await GateTestData.AddAtAsync(database, "Disabled", 1, isEnabled: false);

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		string toggle = GateHtml.OpenTag(html, "button", $"aria-label=\"Enable {gate}\"");
		Assert.Equal("switch", GateHtml.AttributeValue(toggle, "role"));
		Assert.Equal(expectedChecked, GateHtml.AttributeValue(toggle, "aria-checked"));
		Assert.False(GateHtml.HasAttribute(toggle, "disabled"), "The switch should be usable when nothing is running.");
	}

	[Fact]
	public async Task OnlyADisabledGatesRowIsMuted()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Enabled", 0, isEnabled: true);
		await GateTestData.AddAtAsync(database, "Disabled", 1, isEnabled: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		IReadOnlyList<GateRenderedElement> rows = await host.FindElementsAsync("li");

		Assert.Equal(["gate-row", "gate-row gate-row-disabled"], rows.Select(row => row.Attribute("class")));
	}

	[Fact]
	public async Task OffersEditForEveryGateAndAddGateOnceAndNoDeleteBesideTheGates()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");

		string html = await GateRenderHost.RenderAsync<GateTable>(database);

		Assert.Matches(@"aria-label=""Edit Build""", html);
		Assert.Matches(@"aria-label=""Edit Tests""", html);
		Assert.Equal(1, GateHtml.CountIconButtons(html, "Add gate"));
		Assert.Empty(Regex.Matches(html, @"aria-label=""(Delete|Confirm deleting|Cancel deleting) "));
	}

	[Fact]
	public async Task LeavesTheEditButtonsAnIconOnlyAndTheirNamesToTheAriaLabels()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();

		GateRenderedElement edit = await host.FindButtonAsync("Edit Build");

		Assert.Equal(string.Empty, edit.Text);
	}

	private static async Task CreateAsync(TestDatabase database, GateInput input)
	{
		ServiceResult<Gate> result = await new GateService(database.ContextFactory).CreateAsync(input, TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, result.Error);
	}
}
