using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Mcp;

internal sealed class ProjectTools(IProjectCatalog catalog, ProjectSessions sessions, ProjectSetupService setup, PipelineController pipeline)
{
	[McpServerTool(ReadOnly = true), Description("List Pyxie projects, including their IDs and which project is open.")]
	public async Task<object> ListProjectsAsync(CancellationToken cancellationToken) => (await catalog.ListAsync(cancellationToken)).Select(McpProject.From).ToArray();

	[McpServerTool, Description("Import a local Git worktree by absolute path and open it. Close the current project first.")]
	public async Task<object> ImportRepositoryAsync(string folder, CancellationToken cancellationToken) => McpRepository.From((await setup.ImportProjectAsync(RepositorySource.Folder, folder, cancellationToken)).Value());

	[McpServerTool, Description("Clone an HTTP, HTTPS, Git or SSH repository and open it. For SCP-style Git addresses use ssh://user@host/path. Close the current project first.")]
	public async Task<object> CloneRepositoryAsync(string url, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? source) || source.Scheme is not ("http" or "https" or "git" or "ssh") || source.Host.Length == 0)
		{
			throw new McpException("Provide an HTTP, HTTPS, Git or SSH repository URL. Use import_repository for a local folder.");
		}

		return McpRepository.From((await setup.ImportProjectAsync(RepositorySource.Url, url, cancellationToken)).Value());
	}

	[McpServerTool, Description("Open a project while the pipeline is stopped. Closes the previously open project.")]
	public async Task<object> OpenProjectAsync(int projectId, CancellationToken cancellationToken) => McpProject.From((await sessions.OpenAsync(projectId, cancellationToken)).Value());

	[McpServerTool, Description("Close the named open project while the pipeline is stopped.")]
	public async Task<string> CloseProjectAsync(int projectId, CancellationToken cancellationToken) => (await sessions.CloseAsync(projectId, cancellationToken)).Done();

	[McpServerTool(Destructive = true), Description("Permanently remove a closed project and its Pyxie-owned history, hub, images and media.")]
	public async Task<string> RemoveProjectAsync(int projectId, CancellationToken cancellationToken) => (await sessions.RemoveAsync(projectId, cancellationToken)).Done();

	[McpServerTool(ReadOnly = true), Description("Inspect whether the pipeline is running, active round count, and the reason it stopped.")]
	public object GetPipeline() => new { pipeline.IsRunning, pipeline.RunningRounds, pipeline.StopReason };

	[McpServerTool, Description("Start or stop the open project's pipeline. Stopping interrupts active rounds.")]
	public string SetPipelineRunning(int projectId, bool running)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (running)
		{
			return pipeline.Start().Done();
		}

		pipeline.Stop();
		return "Stopped.";
	}
}
