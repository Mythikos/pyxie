using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Web.Components.Settings;

/// <summary>The open project's sandbox settings: the Dockerfile of its image, a build of that image on request, and whether the sign-ins agents' sandboxes get are on this machine (see <c>docs/sandboxing.md</c>). Builds belong to <see cref="SandboxImageBuilds"/>, not to the page: one keeps running when the person leaves, the page shows it again when they come back, and it shows the pipeline's builds as well. Built is asked of the engine, so it shows whenever the image for the saved Dockerfile is on the machine, however and whenever it was built, and not once anything removed it. A failed build of the saved Dockerfile shows until a later build of it succeeds, whatever other projects built since; Pyxie remembers it only while it runs. Both can show: a rebuild that failed leaves the image it would have replaced.</summary>
public partial class SandboxSettingsEditor : IDisposable
{
	private readonly CancellationTokenSource _disposed = new();
	private SandboxSettings _settings = new();
	private string _dockerfile = string.Empty;
	private string? _error;

	// The tag of the image for the saved Dockerfile, when the engine had it at the last lookup, and which lookup is the latest: a save and a build that ended can each start one, and only the latest counts.
	private string? _imageTag;
	private int _imageLookups;
	private SandboxSignInStatus? _signIns;
	private bool _saving;

	/// <summary>Gets or sets the sandbox settings service, supplied by the framework.</summary>
	[Inject]
	public required ISandboxSettingsService SettingsService { get; set; }

	/// <summary>Gets or sets the image builds, which build the image and report every build, supplied by the framework.</summary>
	[Inject]
	public required SandboxImageBuilds ImageBuilds { get; set; }

	/// <summary>Gets or sets the sign-ins agents' sandboxes get, supplied by the framework.</summary>
	[Inject]
	public required ISandboxSignIns SignIns { get; set; }

	private bool HasChanges => Normalized(_dockerfile) != Normalized(_settings.Dockerfile);

	internal bool IsEditing => HasChanges || _saving;

	private string? ProjectKey => OpenProject.Current is Project project ? SandboxNames.ProjectKey(project.Folder) : null;

	// This project's build that runs now, or the last of its builds that finished; another project's says nothing about this one.
	private SandboxImageBuild? ProjectBuild => ProjectKey is string projectKey ? ImageBuilds.Latest(projectKey) : null;

	// A person's build that waits for the pipeline's is building as far as they are concerned.
	private bool IsBuilding => ProjectBuild is { IsRunning: true } || CancellableBuild is not null;

	private SandboxImageBuild? CancellableBuild => ImageBuilds.PersonBuild is SandboxImageBuild build && build.ProjectKey == ProjectKey ? build : null;

	// A build of the project that runs shows only itself; the image it would replace is still there meanwhile.
	private string? BuiltTag => IsBuilding ? null : _imageTag;

	// A build's outcome describes the Dockerfile it built, so it is shown only while that Dockerfile is the saved one.
	private SandboxImageBuild? FailedBuild => ProjectBuild is { State: SandboxImageBuildState.Failed } build && build.Builds(build.ProjectKey, _settings.Dockerfile) ? build : null;

	// A build uses the saved Dockerfile, so unsaved changes are saved first. Builds run one at a time, so another project's build holds this one back; the disabled button says why.
	private string? BuildBlockedText => HasChanges
		? "Save the Dockerfile first."
		: ImageBuilds.Building is SandboxImageBuild other && other.ProjectKey != ProjectKey ? "Another project's image is building." : null;

	/// <inheritdoc/>
	public void Dispose()
	{
		ImageBuilds.Changed -= OnBuildsChanged;
		_disposed.Cancel();
		_disposed.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnInitializedAsync()
	{
		ImageBuilds.Changed += OnBuildsChanged;
		_signIns = SignIns.GetStatus();
		await LoadAsync();

		// Prerendering waits for this method, and an engine that is starting can be slow to answer, so the image is asked for once the page is interactive; an interactive page renders while it waits.
		if (RendererInfo.IsInteractive)
		{
			await FindImageAsync();
		}
	}

	private static string Normalized(string text)
	{
		return text.ReplaceLineEndings("\n").Trim();
	}

	private void OnBuildsChanged()
	{
		_ = InvokeAsync(OnBuildsChangedAsync);
	}

	// A build of the project that ended can have made its image; while one runs, the image is not shown anyway.
	private async Task OnBuildsChangedAsync()
	{
		StateHasChanged();
		if (!IsBuilding)
		{
			await FindImageAsync();
			StateHasChanged();
		}
	}

	// Asks the engine for the image of the saved Dockerfile. An engine that does not answer has no image to show; Build says what is wrong when it is pressed. A page that is gone asks nothing; this runs on the page's own context, as Dispose does, so the check holds until the lookup starts.
	private async Task FindImageAsync()
	{
		if (_disposed.IsCancellationRequested)
		{
			return;
		}

		if (OpenProject.Current is not Project project)
		{
			_imageTag = null;
			return;
		}

		int lookup = ++_imageLookups;
		string? tag;
		try
		{
			tag = await ImageBuilds.FindImageAsync(project.Folder, _settings.Dockerfile, _disposed.Token);
		}
		catch (SandboxUnavailableException)
		{
			tag = null;
		}
		catch (OperationCanceledException) when (_disposed.IsCancellationRequested)
		{
			return;
		}

		if (lookup == _imageLookups)
		{
			_imageTag = tag;
		}
	}

	private async Task LoadAsync()
	{
		_settings = await SettingsService.GetAsync(CancellationToken.None);
		_dockerfile = _settings.Dockerfile;
	}

	private async Task SaveAsync()
	{
		using var lease = AcquireProject(out _error);
		if (lease is null || _saving || !HasChanges)
		{
			return;
		}

		_saving = true;
		_error = null;
		try
		{
			ServiceResult<SandboxSettings> saved = await SettingsService.SaveAsync(new SandboxSettingsInput(_dockerfile), _settings.Version, CancellationToken.None);
			if (saved.Succeeded && saved.Value is not null)
			{
				_settings = saved.Value;
				_dockerfile = saved.Value.Dockerfile;
				_imageTag = null;
				await FindImageAsync();
			}
			else
			{
				_error = saved.Error;
			}
		}
		finally
		{
			_saving = false;
		}
	}

	// Rebuilds from scratch in the background, which also brings the agents' command lines in Pyxie's base image up to date. The button is disabled while any build runs, so a refusal only means another began a moment ago, which the page then shows.
	private Task Build()
	{
		using var lease = AcquireProject(out _);
		if (lease is not null && !HasChanges && !_saving)
		{
			_error = null;
			ImageBuilds.TryStart(lease.Project.Folder, _settings.Dockerfile);
		}

		return Task.CompletedTask;
	}

	private void Cancel(MouseEventArgs press, SandboxImageBuild shown)
	{
		if (press.Detail <= 1 && !_disposed.IsCancellationRequested)
		{
			ImageBuilds.Cancel(shown.ProjectKey, shown.Id);
		}
	}
}
