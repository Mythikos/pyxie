using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Web.Components.Settings;

/// <summary>The dialog an import of a settings file opens in: each section the file holds as a choice, all chosen at first, with what importing it removes, or why it cannot be imported. Import replaces the chosen sections, all or nothing; a refusal stays in the dialog with its reasons and what the import would do now. A file that could not be read shows why, with Close only.</summary>
public partial class SettingsImportDialog : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();

	// The file the dialog was opened with, which Import imports, and whether the dialog can still act.
	private readonly EditorOpening<ServiceResult<SettingsImportOpening>> _opening = new();
	private readonly HashSet<TransferSection> _picked = [];
	private ProjectSettings? _settings;
	private SettingsImportPreview? _preview;
	private bool _busy;
	private string? _error;

	/// <summary>Gets or sets a value indicating whether the dialog is shown.</summary>
	[Parameter]
	public bool Visible { get; set; }

	/// <summary>Gets or sets the callback raised when the dialog asks to be hidden.</summary>
	[Parameter]
	public EventCallback<bool> VisibleChanged { get; set; }

	/// <summary>Gets or sets the file the dialog is shown for: its settings and their preview, or why it could not be read.</summary>
	[Parameter]
	public ServiceResult<SettingsImportOpening>? Opening { get; set; }

	/// <summary>Gets or sets the callback raised after an import, with the sections it changed.</summary>
	[Parameter]
	public EventCallback<IReadOnlySet<TransferSection>> Imported { get; set; }

	/// <summary>Gets or sets the settings transfer service, supplied by the framework.</summary>
	[Inject]
	public required ISettingsTransferService Transfer { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override void OnParametersSet()
	{
		if (!_opening.Follow(Visible, Opening))
		{
			return;
		}

		_settings = _opening.Subject?.Value?.Settings;
		_preview = _opening.Subject?.Value?.Preview;
		_picked.Clear();

		// Every section the file holds starts chosen, a blocked one too: the person decides to leave it out, and Import waits until they do.
		_picked.UnionWith(_preview?.Sections.Select(section => section.Section) ?? []);
		_busy = false;
		_error = null;
	}

	// Choices and Import act only while the dialog is open and nothing is running, whatever the browser shows: a click the person made while the last one ran is handled once it has ended.
	private bool CanAct => _opening.IsOpen && !_busy;

	private bool CanImport => CanAct && _settings is not null && _preview is SettingsImportPreview preview && _picked.Count > 0 && _picked.All(section => preview.Find(section) is { CanImport: true });

	private void SetPicked(TransferSection section, bool picked)
	{
		if (!CanAct)
		{
			return;
		}

		if (picked)
		{
			_picked.Add(section);
		}
		else
		{
			_picked.Remove(section);
		}
	}

	// A press imports what the dialog showed when it was pressed. The wrapper handles it, drawn with the preview of that render, so a press the browser sent before it showed a refreshed preview, or one it counts as part of a double click, imports nothing the person has not seen.
	private async Task ImportAsync(MouseEventArgs press, SettingsImportPreview? shownPreview)
	{
		if (press.Detail > 1 || !CanImport || !ReferenceEquals(shownPreview, _preview) || _settings is not ProjectSettings settings || _preview is not SettingsImportPreview preview)
		{
			return;
		}

		HashSet<TransferSection> sections = [.. _picked];
		_busy = true;
		_error = null;

		try
		{
			ServiceResult result = await Transfer.ImportAsync(settings, preview, sections, _cts.Token);
			if (result.Succeeded)
			{
				_opening.Finish();
				await Imported.InvokeAsync(sections.Where(section => preview.Find(section)?.Changes.HasChanges == true).ToHashSet());
				await VisibleChanged.InvokeAsync(false);

				return;
			}

			_error = result.Error;

			// The refusal may come from a project that changed since the file was read, so the dialog shows what the import would do now. The choices stay as they were: a section that is blocked now keeps Import waiting until the person leaves it out.
			ServiceResult<SettingsImportPreview> refreshed = await Transfer.PreviewAsync(preview.ProjectId, settings, _cts.Token);
			if (refreshed is { Succeeded: true, Value: SettingsImportPreview current })
			{
				_preview = current;
				_picked.IntersectWith(current.Sections.Select(section => section.Section));
			}
			else
			{
				_preview = null;
				_error = $"{_error}\n{refreshed.Error}";
			}
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_busy = false;
		}
	}

	private async Task CancelAsync()
	{
		if (!CanAct)
		{
			return;
		}

		await FinishAsync();
	}

	// The modal reports a close the person made, with Escape or the backdrop, and repeats late, 150 ms after its animation, the showing and hiding its parent asked for. Only a close made while the dialog is open is passed on.
	private async Task OnModalVisibleChangedAsync(bool visible)
	{
		if (!visible && _opening.IsOpen && !_busy)
		{
			await FinishAsync();
		}
	}

	private async Task FinishAsync()
	{
		_opening.Finish();
		await VisibleChanged.InvokeAsync(false);
	}
}
