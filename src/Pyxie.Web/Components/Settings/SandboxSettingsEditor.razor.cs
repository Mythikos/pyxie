using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Web.Components.Settings;

/// <summary>The open project's sandbox settings: the Dockerfile of its image, a build of that image on request, and whether the sign-ins agents' sandboxes get are on this machine (see <c>docs/sandboxing.md</c>). Which image was built is known only from a build in this session, so nothing says so before one.</summary>
public partial class SandboxSettingsEditor
{
	// The end of a failed build's log is where docker says what went wrong.
	private const int LogTailLines = 20;

	private SandboxSettings _settings = new();
	private string _dockerfile = string.Empty;
	private string? _error;

	// The tag of the image the last build made, once that build succeeded, and the end of its log when it failed. Saving makes both obsolete, because they describe the Dockerfile as it was.
	private string? _builtTag;
	private string? _buildLog;
	private SandboxSignInStatus? _signIns;
	private bool _busy;
	private bool _building;

	/// <summary>Gets or sets the sandbox settings service, supplied by the framework.</summary>
	[Inject]
	public required ISandboxSettingsService SettingsService { get; set; }

	/// <summary>Gets or sets the sandboxes, which build the image, supplied by the framework.</summary>
	[Inject]
	public required ISandboxes Sandboxes { get; set; }

	/// <summary>Gets or sets the sign-ins agents' sandboxes get, supplied by the framework.</summary>
	[Inject]
	public required ISandboxSignIns SignIns { get; set; }

	/// <summary>Gets or sets the open project, whose image this is, supplied by the framework.</summary>
	[Inject]
	public required OpenProject OpenProject { get; set; }

	/// <inheritdoc/>
	protected override async Task OnInitializedAsync()
	{
		_signIns = SignIns.GetStatus();
		await LoadAsync();
	}

	private bool HasChanges => Normalized(_dockerfile) != Normalized(_settings.Dockerfile);

	// A build uses the saved Dockerfile, so unsaved changes are saved first; the disabled button says so.
	private string? BuildBlockedText => HasChanges ? "Save the Dockerfile first." : null;

	private static string Normalized(string text)
	{
		return text.ReplaceLineEndings("\n").Trim();
	}

	private static string? Tail(string path)
	{
		if (!File.Exists(path))
		{
			return null;
		}

		string[] lines = File.ReadAllLines(path);

		return string.Join('\n', lines.Skip(Math.Max(0, lines.Length - LogTailLines)));
	}

	private async Task LoadAsync()
	{
		_settings = await SettingsService.GetAsync(CancellationToken.None);
		_dockerfile = _settings.Dockerfile;
	}

	private async Task SaveAsync()
	{
		_busy = true;
		_error = null;
		try
		{
			ServiceResult<SandboxSettings> saved = await SettingsService.SaveAsync(new SandboxSettingsInput(_dockerfile), _settings.Version, CancellationToken.None);
			if (saved.Succeeded && saved.Value is not null)
			{
				_settings = saved.Value;
				_dockerfile = saved.Value.Dockerfile;
				_builtTag = null;
				_buildLog = null;
			}
			else
			{
				_error = saved.Error;
			}
		}
		finally
		{
			_busy = false;
		}
	}

	// Rebuilds from scratch, which also brings the agents' command lines in Pyxie's base image up to date.
	private async Task BuildAsync()
	{
		if (OpenProject.Current is not Project project)
		{
			return;
		}

		_busy = true;
		_building = true;
		_error = null;
		_builtTag = null;
		_buildLog = null;
		string logPath = WorkspaceLayout.ImageLogPath(project.Folder);
		try
		{
			ServiceResult<string> built = await Sandboxes.EnsureImageAsync(new SandboxImageRequest(SandboxNames.ProjectKey(project.Folder), _settings.Dockerfile, Rebuild: true, logPath), CancellationToken.None);

			if (built.Succeeded)
			{
				_builtTag = built.Value ?? string.Empty;
			}
			else
			{
				_error = built.Error;
				_buildLog = Tail(logPath);
			}
		}
		catch (SandboxUnavailableException exception)
		{
			_error = exception.Message;
		}
		finally
		{
			_busy = false;
			_building = false;
		}
	}
}
