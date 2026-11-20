using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Runtime.Orchestration;
using Pyxie.Web.Components.Settings.Models;
using Pyxie.Web.Hosting;

namespace Pyxie.Web.Components.Settings;

/// <summary>Export and Import of the project's settings: Export downloads them as a settings file; Import picks one, reads it, works out what importing it would do, and opens the import dialog with that.</summary>
public partial class SettingsTransfer : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();
	private FileInput? _fileInput;
	private ServiceResult<SettingsImportOpening>? _opening;
	private bool _dialogVisible;
	private bool _reading;

	/// <summary>Gets or sets the project the page shows, whose settings are exported and imported into.</summary>
	[Parameter]
	[EditorRequired]
	public required Project Project { get; set; }

	/// <summary>Gets or sets the callback raised after an import, with the sections it changed.</summary>
	[Parameter]
	public EventCallback<IReadOnlySet<TransferSection>> Imported { get; set; }

	/// <summary>Gets or sets the settings transfer service, supplied by the framework.</summary>
	[Inject]
	public required ISettingsTransferService Transfer { get; set; }

	/// <summary>Gets or sets the announcements of changes to cards, supplied by the framework.</summary>
	[Inject]
	public required PipelineActivity Activity { get; set; }

	// The link names the project, so a page left open after another project was opened downloads nothing instead of that project's settings.
	private string ExportHref => $"{PyxieEndpointRouteBuilderExtensions.SettingsExportPath}?project={Project.Id}";

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	// The file input is hidden, which takes it out of the tab order and the accessibility tree; Import opens its picker. A browser opens a picker only within a few seconds of the person's press, which the round trip of a locally run app is well within; Blazorise swallows a refusal, so a picker that does not open leaves the page as it was, and the next press opens it.
	private async Task ChooseFileAsync()
	{
		if (_reading || _dialogVisible || _fileInput is null)
		{
			return;
		}

		await _fileInput.ShowPicker();
	}

	// Resetting the input after each read lets the same file be picked again, and raises a change with no files, which is ignored, as is a pick while a file is being read or the dialog is open.
	private async Task OnFileChangedAsync(FileChangedEventArgs e)
	{
		if (e.Files.Length == 0 || _reading || _dialogVisible)
		{
			return;
		}

		_reading = true;
		try
		{
			ServiceResult<ProjectSettings> read = await SettingsFileUpload.ReadAsync(e.Files[0], _cts.Token);
			if (read is not { Succeeded: true, Value: ProjectSettings settings })
			{
				Open(ServiceResult<SettingsImportOpening>.Failure(read.Error ?? "The file could not be read."));
				return;
			}

			ServiceResult<SettingsImportPreview> preview = await Transfer.PreviewAsync(Project.Id, settings, _cts.Token);
			Open(preview is { Succeeded: true, Value: SettingsImportPreview previewed } ? ServiceResult<SettingsImportOpening>.Success(new SettingsImportOpening(settings, previewed)) : ServiceResult<SettingsImportOpening>.Failure(preview.Error ?? "The file could not be read."));
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
		}
		finally
		{
			_reading = false;
			await ResetFileInputAsync();
		}
	}

	private void Open(ServiceResult<SettingsImportOpening> opening)
	{
		_opening = opening;
		_dialogVisible = true;
	}

	private void OnDialogVisibleChanged(bool visible)
	{
		_dialogVisible = visible;
	}

	// An import that changed agents or categories may have released finished cards and changes the chips cards show, so the board reads its cards again.
	private async Task OnImportedAsync(IReadOnlySet<TransferSection> changed)
	{
		if (changed.Contains(TransferSection.Agents) || changed.Contains(TransferSection.Categories))
		{
			Activity.NotifyCardsChanged();
		}

		await Imported.InvokeAsync(changed);
	}

	private async Task ResetFileInputAsync()
	{
		if (_fileInput is null)
		{
			return;
		}

		try
		{
			await _fileInput.Reset();
		}
		catch (JSDisconnectedException)
		{
		}
	}
}
