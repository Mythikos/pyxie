using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;

namespace Pyxie.Web.Mcp;

internal sealed class RepositoryTools(PipelineController pipeline, RepositoryBrowser browser, IRepositoryService repositories)
{
	[McpServerTool(ReadOnly = true), Description("Show the imported repository, target branch and available branches.")]
	public async Task<object> GetRepositoryAsync(int projectId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		RepositoryOverview overview = await browser.GetOverviewAsync(cancellationToken) ?? throw new McpException("No repository is connected.");
		McpRepository repository = McpRepository.From(projectId, overview.Repository);
		return new { Name = SourceNames.NameOf(repository.Origin), Repository = repository, overview.Branches };
	}

	[McpServerTool(ReadOnly = true), Description("List a directory in the project's Git tree at a branch or commit. Use an empty path for the root.")]
	public async Task<object> ListRepositoryFilesAsync(int projectId, string revision, CancellationToken cancellationToken, string path = "")
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return await browser.ListAsync(await RepositoryAsync(cancellationToken), revision, path, cancellationToken) ?? throw new McpException("Directory or revision not found.");
	}

	[McpServerTool(ReadOnly = true), Description("Read a tracked text file from the project's Git tree at a branch or commit.")]
	public async Task<object> ReadRepositoryFileAsync(int projectId, string revision, string path, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return await browser.ReadAsync(await RepositoryAsync(cancellationToken), revision, path, cancellationToken) ?? throw new McpException("File or revision not found.");
	}

	[McpServerTool(ReadOnly = true), Description("List a zero-based page of first-parent Git history, newest first.")]
	public async Task<object> ListRepositoryCommitsAsync(int projectId, string revision, CancellationToken cancellationToken, int page = 0)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (page < 0 || page > int.MaxValue / RepositoryBrowser.CommitsPerPage)
		{
			throw new McpException("Invalid history page.");
		}

		return await browser.GetHistoryAsync(await RepositoryAsync(cancellationToken), revision, page, cancellationToken);
	}

	[McpServerTool(ReadOnly = true), Description("Inspect a commit and its patch in the imported repository.")]
	public async Task<object> GetRepositoryCommitAsync(int projectId, string commit, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return await browser.GetCommitAsync(await RepositoryAsync(cancellationToken), commit, cancellationToken) ?? throw new McpException("Commit not found.");
	}

	private async Task<Repository> RepositoryAsync(CancellationToken cancellationToken) => await repositories.GetAsync(cancellationToken) ?? throw new McpException("No repository is connected.");
}
