using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Mcp;

internal sealed class ProjectSettingsTools(PipelineController pipeline, PipelineActivity activity, IAgentService agents, ICategoryService categories, IGateService gates, ISandboxSettingsService sandbox)
{
	[McpServerTool(ReadOnly = true), Description("Read agent definitions, categories, ordered gates and the sandbox Dockerfile with IDs, item revisions and the Dockerfile version for editing.")]
	public async Task<object> GetProjectSettingsAsync(int projectId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return new { Agents = (await agents.ListAsync(cancellationToken)).Select(Describe).ToArray(), Categories = (await categories.ListAsync(cancellationToken)).Select(Describe).ToArray(), Gates = (await gates.ListAsync(cancellationToken)).Select(Describe).ToArray(), Sandbox = await sandbox.GetAsync(cancellationToken) };
	}

	[McpServerTool, Description("Create an agent or replace the definition by supplying agentId and its expectedRevision from get_project_settings. Choose DeepSeek and ApiKey to use the machine's DeepSeek API key. Omitted command and arguments use the harness defaults. Use get_harness_setup for credential instructions.")]
	public async Task<object> SaveAgentAsync(int projectId, string name, HarnessKind harness, AgentAuthentication authentication, bool canWork, bool canReview, CancellationToken cancellationToken, int? agentId = null, string? command = null, string? arguments = null, bool canMerge = false, string? expectedRevision = null)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		AgentInput input = new(name, harness, command ?? HarnessInfo.DefaultCommand(harness), arguments ?? HarnessInfo.DefaultArguments(harness), canWork, canReview, canMerge) { Authentication = authentication };
		Agent agent = (agentId.HasValue ? await agents.UpdateIfCurrentAsync(agentId.Value, input, RevisionForEdit(expectedRevision), cancellationToken) : await agents.CreateAsync(input, cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Agents);
		return Describe(agent);
	}

	[McpServerTool, Description("Assign the project's merge agent in one change. Use null to clear the role.")]
	public async Task<string> SetMergeAgentAsync(int projectId, int? agentId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string result = (await agents.SetMergeAgentAsync(agentId, cancellationToken)).Done();
		activity.NotifySettingsChanged(projectId, TransferSection.Agents);
		return result;
	}

	[McpServerTool(Destructive = true), Description("Delete an agent under the same assignment rules as Settings.")]
	public async Task<string> DeleteAgentAsync(int projectId, int agentId, string expectedRevision, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string result = (await agents.DeleteIfCurrentAsync(agentId, RevisionForEdit(expectedRevision), cancellationToken)).Done();
		activity.NotifySettingsChanged(projectId, TransferSection.Agents);
		return result;
	}

	[McpServerTool, Description("Create a gate or replace the fields of gateId using its expectedRevision from get_project_settings. A new gate runs last. Commands run in the project sandbox.")]
	public async Task<object> SaveGateAsync(int projectId, GateInput gate, CancellationToken cancellationToken, int? gateId = null, string? expectedRevision = null)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		Gate saved = (gateId.HasValue ? await gates.UpdateIfCurrentAsync(gateId.Value, gate, RevisionForEdit(expectedRevision), cancellationToken) : await gates.CreateAsync(gate, cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Gates);
		return Describe(saved);
	}

	[McpServerTool, Description("Move a gate to a zero-based position in the execution order. Supply the ordered gate IDs from get_project_settings in expectedOrder.")]
	public async Task<string> MoveGateAsync(int projectId, int gateId, int position, int[] expectedOrder, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string result = (await gates.MoveIfOrderMatchesAsync(gateId, position, expectedOrder ?? throw new McpException("Supply the ordered gate IDs from get_project_settings."), cancellationToken)).Done();
		activity.NotifySettingsChanged(projectId, TransferSection.Gates);
		return result;
	}

	[McpServerTool(Destructive = true), Description("Delete a gate from the project.")]
	public async Task<string> DeleteGateAsync(int projectId, int gateId, string expectedRevision, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string result = (await gates.DeleteIfCurrentAsync(gateId, RevisionForEdit(expectedRevision), cancellationToken)).Done();
		activity.NotifySettingsChanged(projectId, TransferSection.Gates);
		return result;
	}

	[McpServerTool, Description("Create a category with a #RRGGBB chip color.")]
	public async Task<object> CreateCategoryAsync(int projectId, string name, string color, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		Category category = (await categories.CreateAsync(name, ParseColor(color), cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Categories);
		return Describe(category);
	}

	[McpServerTool, Description("Rename a category in place, retaining its card assignments.")]
	public async Task<object> RenameCategoryAsync(int projectId, int categoryId, string name, string expectedRevision, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		Category category = (await categories.UpdateIfCurrentAsync(categoryId, name, null, RevisionForEdit(expectedRevision), cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Categories);
		return Describe(category);
	}

	[McpServerTool, Description("Change a category's chip color to #RRGGBB.")]
	public async Task<object> SetCategoryColorAsync(int projectId, int categoryId, string color, string expectedRevision, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		Category category = (await categories.UpdateIfCurrentAsync(categoryId, null, ParseColor(color), RevisionForEdit(expectedRevision), cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Categories);
		return Describe(category);
	}

	[McpServerTool(Destructive = true), Description("Delete an unused category.")]
	public async Task<string> DeleteCategoryAsync(int projectId, int categoryId, string expectedRevision, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string result = (await categories.DeleteIfCurrentAsync(categoryId, RevisionForEdit(expectedRevision), cancellationToken)).Done();
		activity.NotifySettingsChanged(projectId, TransferSection.Categories);
		return result;
	}

	private static string RevisionForEdit(string? revision) => !string.IsNullOrWhiteSpace(revision) ? revision : throw new McpException("Supply the item's revision from get_project_settings as expectedRevision.");

	private static HexColor ParseColor(string color) => HexColor.TryParse(color, out HexColor parsed) ? parsed : throw new McpException("Use a color in #RRGGBB format.");

	private static object Describe(Agent agent) => new { agent.Id, agent.Name, agent.Harness, agent.Authentication, agent.Command, agent.Arguments, agent.CanWork, agent.CanReview, agent.CanMerge, Revision = SettingsRevision.Of(agent) };

	private static object Describe(Gate gate) => new { gate.Id, gate.Name, gate.Command, gate.Arguments, gate.TimeoutMinutes, gate.IsEnabled, gate.SortOrder, Revision = SettingsRevision.Of(gate) };

	private static object Describe(Category category) => new { category.Id, category.Name, Color = category.Color.ToString(), Revision = SettingsRevision.Of(category) };
}
