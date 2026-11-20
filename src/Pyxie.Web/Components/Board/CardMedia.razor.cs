using System.Globalization;
using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Web.Components.Board;

/// <summary>The media belonging to one opening of a card. Uploads remain staged until the card is saved.</summary>
public partial class CardMedia : IAsyncDisposable
{
	private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly HashSet<Guid> _selected = [];
	private readonly HashSet<Guid> _original = [];
	private IReadOnlyList<MediaFile> _files = [];
	private IJSObjectReference? _module;
	private IJSObjectReference? _picker;
	private DotNetObjectReference<CardMedia>? _receiver;
	private ElementReference _input;
	private ElementReference _section;
	private Guid? _uploadId;
	private MediaFile? _preview;
	private string? _error;
	private string? _uploadName;
	private int _progress;
	private int _shownGroups = 3;
	private bool _ready;
	private bool _uploading;
	private bool _uploadFailed;
	private bool _disposed;
	private bool _focusSection;

	/// <summary>Gets or sets the project this opening belongs to.</summary>
	[Parameter] public required Project Project { get; set; }

	/// <summary>Gets or sets the card this opening belongs to, or null for a new card.</summary>
	[Parameter] public WorkCard? Card { get; set; }

	/// <summary>Gets or sets whether context can be changed.</summary>
	[Parameter] public bool ReadOnly { get; set; }

	/// <summary>Gets or sets whether the parent is saving.</summary>
	[Parameter] public bool Busy { get; set; }

	/// <summary>Gets or sets the recent rounds used to label output.</summary>
	[Parameter] public IReadOnlyList<CardRound> Rounds { get; set; } = [];

	/// <summary>Gets or sets the callback that updates the parent's save controls.</summary>
	[Parameter] public EventCallback Changed { get; set; }

	/// <summary>Gets or sets the media service.</summary>
	[Inject] public required ICardMediaService MediaService { get; set; }

	/// <summary>Gets or sets JavaScript interop for streamed HTTP uploads.</summary>
	[Inject] public required IJSRuntime JavaScript { get; set; }

	/// <summary>Gets whether all selected context is ready to save.</summary>
	public bool CanSave => _ready && !_uploading && !_uploadFailed && !_disposed;

	/// <summary>Gets whether the context selection differs from the saved card.</summary>
	public bool HasChanges => !_original.SetEquals(_selected);

	/// <summary>Gets whether there are files to display.</summary>
	public bool HasFiles => _files.Count > 0;

	private List<MediaFile> ContextFiles => _files.Where(file => file.RoundId is null && file.RemovedUtc is null && (ReadOnly || _selected.Contains(file.Id))).ToList();
	private List<IGrouping<int?, MediaFile>> OutputGroups => _files.Where(file => file.RoundId is not null).GroupBy(file => file.RoundId).OrderByDescending(group => group.Key).ToList();

	/// <summary>Adds the staged selection to the card's atomic save.</summary>
	/// <param name="input">The edited card fields.</param>
	/// <returns>The fields with the upload session and selection.</returns>
	public CardInput Apply(CardInput input) => input with { Media = _uploadId is Guid id ? new MediaEdit(id, [.. _selected]) : null };

	/// <inheritdoc/>
	protected override async Task OnInitializedAsync()
	{
		try
		{
			await RefreshAsync();
			if (!ReadOnly)
			{
				ServiceResult<CardMediaSession> session = await MediaService.BeginAsync(Project.Id, Card?.Id, Card?.Version, _lifetime.Token);
				if (session.Value is not CardMediaSession opened)
				{
					_error = session.Error;
					return;
				}
				_uploadId = opened.UploadId;
				foreach (MediaFile file in opened.Files)
				{
					_original.Add(file.Id);
					_selected.Add(file.Id);
				}
			}
			_ready = true;
		}
		catch (OperationCanceledException) when (_disposed)
		{
		}
		finally
		{
			await Changed.InvokeAsync();
		}
	}

	/// <summary>Refreshes published output without discarding an unsaved context selection.</summary>
	/// <returns>A task completing after the listing is refreshed.</returns>
	public async Task RefreshAsync()
	{
		if (Card is null || _disposed)
		{
			return;
		}
		ServiceResult<IReadOnlyList<MediaFile>> result = await MediaService.ListAsync(Project.Id, Card.Id, _lifetime.Token);
		if (result.Value is { } files)
		{
			_files = [.. files, .. _files.Where(file => file.UploadId == _uploadId && file.UploadId is not null)];
		}
		else
		{
			_error = result.Error;
		}
		StateHasChanged();
	}

	/// <inheritdoc/>
	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_disposed)
		{
			return;
		}
		try
		{
			if (_ready && !ReadOnly && _picker is null && _uploadId is Guid id)
			{
				_module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "./media.js");
				_receiver = DotNetObjectReference.Create(this);
				_picker = await _module.InvokeAsync<IJSObjectReference>("bind", _input, _receiver, $"media/{Project.Id}/uploads/{id}");
			}
			if (_focusSection)
			{
				_focusSection = false;
				await _section.FocusAsync(preventScroll: true);
			}
		}
		catch (JSDisconnectedException)
		{
		}
	}

	private async Task ChooseAsync()
	{
		if (_ready && !ReadOnly && !Busy && !_uploading && _picker is not null)
		{
			await _picker.InvokeVoidAsync("choose");
		}
	}

	/// <summary>Accepts an upload batch only while this opening can still edit context.</summary>
	/// <returns>Whether the browser may start uploading.</returns>
	[JSInvokable]
	public async Task<bool> MediaUploadStarted()
	{
		if (_disposed || !_ready || Busy || ReadOnly || _uploading)
		{
			return false;
		}
		_uploading = true;
		await Changed.InvokeAsync();
		StateHasChanged();
		return true;
	}

	/// <summary>Shows transport progress for the current upload.</summary>
	/// <param name="name">The file name.</param>
	/// <param name="progress">The percentage sent.</param>
	[JSInvokable]
	public void MediaUploadProgress(string name, int progress)
	{
		if (!_disposed && _uploading)
		{
			_uploadName = name;
			_progress = Math.Clamp(progress, 0, 100);
			StateHasChanged();
		}
	}

	/// <summary>Displays a staged file returned by the upload endpoint. Save independently validates its identity and ownership.</summary>
	/// <param name="json">The endpoint's file metadata.</param>
	[JSInvokable]
	public void MediaUploadCompleted(string json)
	{
		if (!_disposed && _uploading && JsonSerializer.Deserialize<MediaFile>(json, s_json) is MediaFile file && file.UploadId == _uploadId)
		{
			_files = [.. _files.Where(item => item.Id != file.Id), file];
			_selected.Add(file.Id);
			StateHasChanged();
		}
	}

	/// <summary>Shows a failed upload; saving requires acknowledging the missing file.</summary>
	/// <param name="name">The file name.</param>
	/// <param name="error">The endpoint's refusal.</param>
	[JSInvokable]
	public void MediaUploadFailed(string name, string error)
	{
		if (!_disposed)
		{
			_error = $"{name}: {error}";
			_uploadFailed = true;
			StateHasChanged();
		}
	}

	/// <summary>Releases the save controls after the complete upload batch.</summary>
	/// <returns>A task completing after the parent is notified.</returns>
	[JSInvokable]
	public async Task MediaUploadEnded()
	{
		if (!_disposed)
		{
			_uploading = false;
			await Changed.InvokeAsync();
			StateHasChanged();
		}
	}

	private async Task DismissFailureAsync()
	{
		_uploadFailed = false;
		_error = null;
		await Changed.InvokeAsync();
	}

	private async Task RemoveAsync(MediaFile file, MouseEventArgs args)
	{
		if (CanSave && !Busy && !ReadOnly && file.RoundId is null && args.Detail <= 1)
		{
			_selected.Remove(file.Id);
			await Changed.InvokeAsync();
		}
	}

	private string Href(MediaFile file, bool download = false) => $"media/{Project.Id}/files/{file.Id}?{(file.CardId is int card ? $"card={card}" : $"upload={_uploadId}")}{(download ? "&download=1" : string.Empty)}";
	private static bool CanPreview(MediaFile file) => file.ContentType.StartsWith("image/", StringComparison.Ordinal) || file.ContentType.StartsWith("video/", StringComparison.Ordinal) || file.ContentType.StartsWith("audio/", StringComparison.Ordinal);
	private static string SizeText(long bytes) => bytes >= 1024 * 1024 ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024):0.#} MB") : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1024d):0.#} KB");
	private string RoundLabel(int? id) => Rounds.FirstOrDefault(round => round.Id == id) is CardRound round ? $"{round.Kind} · Round {round.Sequence}" : $"Output · Round {id}";
	private void ShowOlder() => _shownGroups += 3;
	private void Preview(MediaFile file) { _preview = file; _focusSection = true; }
	private void ClosePreview() => DismissPreview();
	private void PreviewFailed() => _error = "This file cannot be previewed here. Download it to open it.";

	/// <summary>Dismisses the preview before the containing dialog handles Escape.</summary>
	/// <returns>Whether a preview was dismissed.</returns>
	public bool DismissPreview()
	{
		if (_preview is null)
		{
			return false;
		}
		_preview = null;
		_focusSection = true;
		StateHasChanged();
		return true;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		_disposed = true;
		await _lifetime.CancelAsync();
		try
		{
			if (_picker is not null)
			{
				await _picker.InvokeVoidAsync("dispose");
				await _picker.DisposeAsync();
			}
			if (_module is not null)
			{
				await _module.DisposeAsync();
			}
		}
		catch (JSDisconnectedException)
		{
		}
		finally
		{
			_receiver?.Dispose();
			_lifetime.Dispose();
			if (_uploadId is Guid id)
			{
				await MediaService.AbandonAsync(Project.Id, id, CancellationToken.None);
			}
		}
	}
}
