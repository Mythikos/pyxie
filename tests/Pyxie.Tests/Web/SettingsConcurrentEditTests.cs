using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class SettingsConcurrentEditTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Agent_AStaleFormOrRoleSwitchPreservesTheChangedCommand(bool roleSwitch)
	{
		await using TestDatabase database = new();
		Agent agent = await TestData.AddAgentAsync(database, "Worker", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		if (!roleSwitch)
		{
			await host.ClickAsync(await host.FindButtonAsync("Edit Worker"));
			await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Builder");
		}

		IAgentService agents = services.GetRequiredService<IAgentService>();
		ResultAssert.Succeeded(await agents.UpdateAsync(agent.Id, AgentInput.From(agent) with { Command = "updated-worker" }, Token));
		if (roleSwitch)
		{
			await host.SetCheckedAsync(await host.FindNamedCheckboxAsync("Worker can work"), false);
		}
		else
		{
			await host.ClickAsync(await host.FindButtonAsync("Save"));
			Assert.True(await host.IsModalOpenAsync(), "A refused save keeps the draft.");
		}

		Agent stored = Assert.Single(await agents.ListAsync(Token));
		Assert.Equal("updated-worker", stored.Command);
		Assert.Contains(await host.AlertsAsync(), message => message.Contains("changed", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Gate_AStaleFormPreservesTheChangedCommandAndItsDraft()
	{
		await using TestDatabase database = new();
		Gate gate = await GateTestData.AddAtAsync(database, "Build", 0);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Build"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Compile");
		IGateService gates = services.GetRequiredService<IGateService>();
		ResultAssert.Succeeded(await gates.UpdateAsync(gate.Id, GateInput.From(gate) with { Command = "new-command" }, Token));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		Assert.Equal("new-command", Assert.Single(await gates.ListAsync(Token)).Command);
		Assert.True(await host.IsModalOpenAsync(), "A refused save keeps the draft.");
		Assert.Contains(await host.AlertsAsync(), message => message.Contains("changed", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Gate_MovingWithAnOutdatedListPreservesTheNewExecutionOrder()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> original = await GateTestData.AddOrderedAsync(database, "Build", "Test", "Package");
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<GateTable>();
		IGateService gates = services.GetRequiredService<IGateService>();
		ResultAssert.Succeeded(await gates.MoveAsync(original["Package"].Id, 1, Token));

		await host.PressKeyAsync(await host.FindButtonAsync("Move Build"), "ArrowDown");

		Assert.Equal(["Build", "Package", "Test"], (await gates.ListAsync(Token)).Select(gate => gate.Name));
		Assert.Contains(await host.AlertsAsync(), message => message.Contains("order changed", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Category_AStaleFormRefusesBothChanges()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<CategoryList>();
		ICategoryService categories = services.GetRequiredService<ICategoryService>();
		Category original = (await categories.ListAsync(Token)).Single(category => category.Name == "Bug");
		await host.ClickAsync(await host.FindButtonAsync("Edit Bug"));
		await host.TypeAsync(Assert.Single(await host.FindElementsAsync("input", input => input.Attribute("aria-label") == "New name for Bug")), "Defect");
		await host.SetCheckedAsync(await host.FindRadioAsync("Green for Bug"), true);
		ResultAssert.Succeeded(await categories.SetColorAsync(original.Id, CategoryColors.Neutral, Token));

		await host.SubmitAsync(Assert.Single(await host.FindElementsAsync("form", form => (form.Attribute("class") ?? string.Empty).StartsWith("category-editor", StringComparison.Ordinal))));

		Category stored = (await categories.ListAsync(Token)).Single(category => category.Id == original.Id);
		Assert.Equal(("Bug", CategoryColors.Neutral), (stored.Name, stored.Color));
		Assert.Contains(await host.AlertsAsync(), message => message.Contains("changed", StringComparison.OrdinalIgnoreCase));
	}
}
