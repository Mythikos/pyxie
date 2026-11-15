using System.Net;
using System.Text.RegularExpressions;

using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Enums;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class AgentTableTests
{
	[Fact]
	public async Task Render_ShowsTheColumnsInOrderAndNamesTheActionsColumnForScreenReadersOnly()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);

		string html = await GateRenderHost.RenderAsync<AgentTable>(database);

		Assert.Equal(["Name", "Harness", "Command", "Work", "Review", "Resolve", "Actions"], HeaderCells(html));
		Assert.Matches(@"<span class=""pyx-visually-hidden""[^>]*>Actions</span>", html);
	}

	[Fact]
	public async Task Render_ShowsTheNameTheHarnessAndTheCommandLineOfEveryAgent()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true, HarnessKind.ClaudeCode, "claude", "-p --dangerously-skip-permissions");
		await TestData.AddAgentAsync(database, "Runner", canWork: true, canReview: false, HarnessKind.Other, "./run.sh", string.Empty);

		string html = await GateRenderHost.RenderAsync<AgentTable>(database);

		Assert.Equal(["Claude", "Claude Code", "claude -p --dangerously-skip-permissions"], Cells(html, "Claude").Take(3));
		Assert.Equal(["Runner", "Other", "./run.sh"], Cells(html, "Runner").Take(3));
	}

	[Fact]
	public async Task Render_MarksTheTokensOfTheCommandLineAndNothingElseInIt()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true, HarnessKind.ClaudeCode, "claude", "-p --name \"{CARD_NAME}\" --branch {BRANCH} --home ${HOME} --json {\"a\":1}");

		string html = await GateRenderHost.RenderAsync<AgentTable>(database);

		Assert.Equal(["{CARD_NAME}", "{BRANCH}"], [.. Regex.Matches(html, @"<span class=""argument-token""[^>]*>([^<]*)</span>").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))]);
		Assert.Equal("claude -p --name \"{CARD_NAME}\" --branch {BRANCH} --home ${HOME} --json {\"a\":1}", Cells(html, "Claude")[2]);
	}

	[Fact]
	public async Task Render_NamesEveryControlOfARowAfterItsAgent()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		foreach (string agent in new[] { "Claude", "Codex" })
		{
			await host.FindNamedCheckboxAsync($"{agent} can work");
			await host.FindNamedCheckboxAsync($"{agent} can review");
			await host.FindRadioAsync($"{agent} resolves merge conflicts");
			await host.FindButtonAsync($"Edit {agent}");
		}

		await host.FindRadioAsync("No agent resolves merge conflicts");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label")?.StartsWith("Delete ", StringComparison.Ordinal) == true);
	}

	[Fact]
	public async Task Render_ShowsTheStoredRolesOfEachAgent()
	{
		await using TestDatabase database = new();
		await AgentTestData.AddAsync(database, "Merger", canWork: false, canReview: false, canMerge: true);
		await AgentTestData.AddAsync(database, "Claude", canWork: true, canReview: true, canMerge: false);
		await AgentTestData.AddAsync(database, "Codex", canWork: true, canReview: false, canMerge: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		Assert.Equal(["false", "false", "true"], await RolesAsync(host, "Merger"));
		Assert.Equal(["true", "true", "false"], await RolesAsync(host, "Claude"));
		Assert.Equal(["true", "false", "false"], await RolesAsync(host, "Codex"));
		Assert.Equal("false", Checked(await host.FindRadioAsync("No agent resolves merge conflicts")));
	}

	[Fact]
	public async Task Render_ChoosesNoneAsTheMergeAgentWhileNoAgentHasTheRole()
	{
		await using TestDatabase database = new();
		await AgentTestData.AddAsync(database, "Claude", canWork: true, canReview: true, canMerge: false);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		Assert.Equal("false", Checked(await host.FindRadioAsync("Claude resolves merge conflicts")));
		Assert.Equal("true", Checked(await host.FindRadioAsync("No agent resolves merge conflicts")));
	}

	[Fact]
	public async Task Render_PutsEveryMergeChoiceInOneGroupSoOnlyOneCanBeChosen()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();

		IReadOnlyList<GateRenderedElement> radios = await host.FindElementsAsync("input", element => element.Attribute("type") == "radio");

		Assert.Equal(3, radios.Count);
		Assert.Single(radios.Select(radio => radio.Attribute("name")).Distinct());
	}

	[Fact]
	public async Task Render_WithNoAgents_ShowsTheHeadingAndAddAgentAndNothingElse()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<AgentTable>(database);

		Assert.Matches(@"<h2\b[^>]*>Agents</h2>", html);
		Assert.Equal(1, GateHtml.CountIconButtons(html, "Add agent"));
		Assert.DoesNotContain("<table", html, StringComparison.Ordinal);
		Assert.DoesNotContain("No agents", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_IsThePanelTheAgentsAnchorPointsAtAndTheHeadingNamesIt()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<AgentTable>(database);

		Assert.Matches(@"<section id=""agents"" class=""pyx-panel[^""]*"" aria-labelledby=""agents-heading""", html);
		Assert.Matches(@"<h2 id=""agents-heading""[^>]*>Agents</h2>", html);
	}

	[Fact]
	public async Task Edit_OpensTheEditorWithTheAgentAndAddAgentOpensItBlank()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await TestData.AddAgentAsync(database, "Codex", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		Assert.False(await host.IsModalOpenAsync(), "The editor is closed until someone asks for it.");

		await host.ClickAsync(await host.FindButtonAsync("Edit Codex"));

		Assert.True(await host.IsModalOpenAsync(), "Edit should open the editor.");
		Assert.Equal("Codex", (await host.FindFieldAsync("_model.Name")).Attribute("value"));
		Assert.Contains("Edit agent", await host.TextsWithClassAsync("ant-modal-title"));

		await host.ClickAsync(await host.FindButtonAsync("Cancel"));
		await host.ClickAsync(await host.FindButtonAsync("Add agent"));

		Assert.True(await host.IsModalOpenAsync(), "Add agent should open the editor.");
		Assert.Equal(string.Empty, (await host.FindFieldAsync("_model.Name")).Attribute("value") ?? string.Empty);
		Assert.Contains("Add agent", await host.TextsWithClassAsync("ant-modal-title"));
	}

	[Fact]
	public async Task SavingTheEditor_ShowsTheChangedAgentInTheTable()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		await using ServiceProvider services = GateRenderHost.CreateServices(database);
		await using GateInteractiveHost host = new(services);
		await host.StartAsync<AgentTable>();
		await host.ClickAsync(await host.FindButtonAsync("Edit Claude"));
		await host.TypeAsync(await host.FindFieldAsync("_model.Name"), "Sonnet");

		await host.ClickAsync(await host.FindButtonAsync("Save"));

		await host.FindButtonAsync("Edit Sonnet");
		Assert.DoesNotContain(await host.ElementsAsync(), element => element.Attribute("aria-label") == "Edit Claude");
	}

	private static string Checked(GateRenderedElement input)
	{
		return input.Attribute("checked") ?? "false";
	}

	// The Work, Review, and Resolve controls of an agent, as "true" or "false" for whether each is ticked or chosen.
	private static async Task<List<string>> RolesAsync(GateInteractiveHost host, string agent)
	{
		return
		[
			Checked(await host.FindNamedCheckboxAsync($"{agent} can work")),
			Checked(await host.FindNamedCheckboxAsync($"{agent} can review")),
			Checked(await host.FindRadioAsync($"{agent} resolves merge conflicts")),
		];
	}

	private static List<string> HeaderCells(string html)
	{
		return [.. Regex.Matches(html, @"<th\b[^>]*>(.*?)</th>", RegexOptions.Singleline).Select(match => Text(match.Groups[1].Value))];
	}

	// The cells of the row of an agent, as text.
	private static List<string> Cells(string html, string agentName)
	{
		foreach (Match row in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Singleline))
		{
			List<string> cells = [.. Regex.Matches(row.Groups[1].Value, @"<td\b[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(match => Text(match.Groups[1].Value))];
			if (cells.Count > 0 && cells[0] == agentName)
			{
				return cells;
			}
		}

		throw new InvalidOperationException($"The table has no row for '{agentName}'.");
	}

	private static string Text(string cellHtml)
	{
		return WebUtility.HtmlDecode(Regex.Replace(cellHtml, "<[^>]*>", string.Empty)).Trim();
	}
}
