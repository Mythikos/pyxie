using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Entities;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Transfer;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Mcp;

internal sealed class SandboxTools(PipelineController pipeline, PipelineActivity activity, ISandboxSettingsService sandbox, SandboxImageBuilds builds, ISandboxSignIns signIns)
{
	[McpServerTool(ReadOnly = true), Description("Explain Pyxie account and API key setup for every supported harness, including dsh, with local credential status, paths and default commands. Other harnesses use a custom command and no managed credential.")]
	public object GetHarnessSetup() => new { Harnesses = HarnessInfo.All.Select(harness => new { Harness = harness, Command = HarnessInfo.DefaultCommand(harness), Arguments = HarnessInfo.DefaultArguments(harness) }).ToArray(), Credentials = HarnessCredentialGuide.From(signIns.GetStatus()) };

	[McpServerTool(ReadOnly = true), Description("Get the base Dockerfile and Pyxie's contract for requesting and publishing browser, terminal and desktop captures.")]
	public static object GetCaptureSetup() => new { Dockerfile = SandboxDefaults.Dockerfile, Guidance = "Extend the project Dockerfile with tools needed by its application. For browser captures install a browser and its automation dependencies. For desktop captures add Xvfb and a screen recorder such as ffmpeg. For terminal output redirect the command to a file. Ask the worker or reviewer for the desired capture in the card description. Run pyxie-attach <file> --caption <text> in the round. Published output belongs to that round and is available from list_card_media." };

	[McpServerTool, Description("Save the project's sandbox Dockerfile using the version from get_project_settings. Build separately.")]
	public async Task<object> SaveDockerfileAsync(int projectId, string dockerfile, int expectedVersion, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		SandboxSettings saved = (await sandbox.SaveAsync(new SandboxSettingsInput(dockerfile), expectedVersion, cancellationToken)).Value();
		activity.NotifySettingsChanged(projectId, TransferSection.Sandbox);
		return saved;
	}

	[McpServerTool, Description("Start building the saved project Dockerfile. Returns the build ID to pass to get_image_build or cancel_image_build.")]
	public async Task<int> BuildProjectImageAsync(int projectId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		SandboxSettings settings = await sandbox.GetAsync(cancellationToken);
		return builds.TryStart(lease.Project.Folder, settings.Dockerfile, out int id) ? id : throw new McpException("An image build is already in progress.");
	}

	[McpServerTool(ReadOnly = true), Description("Check whether the saved Dockerfile's image exists and inspect a build by ID or the latest build for this project.")]
	public async Task<object> GetImageBuildAsync(int projectId, CancellationToken cancellationToken, int? buildId = null)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		string folder = lease.Project.Folder;
		SandboxSettings settings = await sandbox.GetAsync(cancellationToken);
		string? tag = null;
		string? engineError = null;
		try
		{
			tag = await builds.FindImageAsync(folder, settings.Dockerfile, cancellationToken);
		}
		catch (SandboxUnavailableException exception)
		{
			engineError = exception.Message;
		}

		string key = SandboxNames.ProjectKey(folder);
		SandboxImageBuild? build = buildId is int id ? builds.Find(key, id) ?? throw new McpException("That build is unknown or its result is no longer retained.") : builds.Latest(key);
		return new { Image = tag, EngineError = engineError, Build = build is null ? null : new { build.Id, build.Dockerfile, build.RequestedBy, build.State, build.StartedUtc, build.EndedUtc, build.Tag, build.Error, build.LogTail } };
	}

	[McpServerTool, Description("Cancel the identified operator-requested image build of this project.")]
	public string CancelImageBuild(int projectId, int buildId)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		return builds.Cancel(SandboxNames.ProjectKey(lease.Project.Folder), buildId) ? "Cancellation requested." : throw new McpException("That build is no longer running or belongs to the pipeline.");
	}
}
