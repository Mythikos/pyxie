using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class AgentEditorHarnessTests
{
	private const string ClaudeArguments = "-p --dangerously-skip-permissions";
	private const string CodexArguments = "exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check";

	[Fact]
	public async Task ANewAgentStartsWithTheClaudeCodeCommandAndArguments()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);

		await host.StartAsync<AgentEditor>(NewAgent());

		Assert.Equal("claude", await ValueAsync(host, "_model.Command"));
		Assert.Equal(ClaudeArguments, await ValueAsync(host, "_model.Arguments"));
	}

	[Theory]
	[InlineData("Codex", "codex", CodexArguments)]
	[InlineData("DeepSeek", "dsh", "--profile headless --permission-mode danger-full-access --approval-policy never")]
	[InlineData("Other", "", "")]
	public async Task ChoosingAnotherHarnessOnANewAgentFillsInItsCommandAndArguments(string harness, string expectedCommand, string expectedArguments)
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());

		await host.ClickAsync(await host.FindOptionAsync(harness));

		Assert.Equal(expectedCommand, await ValueAsync(host, "_model.Command"));
		Assert.Equal(expectedArguments, await ValueAsync(host, "_model.Arguments"));
	}

	[Fact]
	public async Task ChoosingAHarnessKeepsACommandAndArgumentsAPersonTyped()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Command"), "my-claude");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "-p --model sonnet");

		await host.ClickAsync(await host.FindOptionAsync("Codex"));

		Assert.Equal("my-claude", await ValueAsync(host, "_model.Command"));
		Assert.Equal("-p --model sonnet", await ValueAsync(host, "_model.Arguments"));
	}

	[Fact]
	public async Task ChoosingAHarnessForAStoredAgentUpdatesItsStockCommandAndArguments()
	{
		await using TestDatabase database = new();
		Agent stored = await TestData.AddAgentAsync(database, "Stored", canWork: true, canReview: true, HarnessKind.ClaudeCode, "claude", ClaudeArguments);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(new Dictionary<string, object?> { ["Visible"] = true, ["Agent"] = stored });

		await host.ClickAsync(await host.FindOptionAsync("Codex"));

		Assert.Equal("codex", await ValueAsync(host, "_model.Command"));
		Assert.Equal(CodexArguments, await ValueAsync(host, "_model.Arguments"));
	}

	[Fact]
	public async Task SavingANewAgentStoresTheDefaultArgumentsOfItsHarness()
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentEditor>(NewAgent());
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Codex worker");
		await host.ClickAsync(await host.FindOptionAsync("Codex"));

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		await using ProjectDbContext context = database.CreateContext();
		Agent saved = await context.Agents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
		Assert.Equal(HarnessKind.Codex, saved.Harness);
		Assert.Equal("codex", saved.Command);
		Assert.Equal(CodexArguments, saved.Arguments);
	}

	[Theory]
	[InlineData("Claude Code", HarnessKind.ClaudeCode)]
	[InlineData("Codex", HarnessKind.Codex)]
	[InlineData("DeepSeek", HarnessKind.DeepSeek)]
	public async Task EditingAuthenticationPreservesTheHarnessAndArguments(string label, HarnessKind harness)
	{
		await using TestDatabase database = new();
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Add agent"));
		await host.ClickAsync(await host.FindOptionAsync(label));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Worker");
		await host.TypeAsync(await host.FindFieldAsync("_model.Arguments"), "--custom");
		await host.ClickAsync(await host.FindOptionAsync("API key"));
		await host.ClickAsync(await host.FindButtonAsync("Save"));

		await using ProjectDbContext context = database.CreateContext();
		Agent saved = await context.Agents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
		Assert.Equal((harness, AgentAuthentication.ApiKey, "--custom"), (saved.Harness, saved.Authentication, saved.Arguments));
		await host.ClickAsync(await host.FindButtonAsync("Edit Worker"));
		await host.ClickAsync(await host.FindOptionAsync("Account"));
		await host.ClickAsync(await host.FindButtonAsync("Save"));
		Agent edited = await context.Agents.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
		Assert.Equal((harness, AgentAuthentication.Account, "--custom"), (edited.Harness, edited.Authentication, edited.Arguments));
	}

	private static Dictionary<string, object?> NewAgent()
	{
		return new Dictionary<string, object?> { ["Visible"] = true };
	}

	private static async Task<string> ValueAsync(GateInteractiveHost host, string property)
	{
		return (await host.FindFieldAsync(property)).Attribute("value") ?? string.Empty;
	}
}
