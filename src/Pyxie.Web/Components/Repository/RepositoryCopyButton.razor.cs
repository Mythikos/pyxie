using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Pyxie.Web.Components.Repository;

/// <summary>Copies a repository location, with its own temporary confirmation and timer lifetime.</summary>
public partial class RepositoryCopyButton : ComponentBase, IDisposable
{
	private const int CopyWidthPixels = 92;

	private static readonly TimeSpan s_copiedFor = TimeSpan.FromSeconds(2);

	private readonly CancellationTokenSource _cts = new();

	// The Copy button keeps its width while it says Copied, so the line does not move.
	private readonly IFluentSizingStyle _copyWidth = Width.Px().Min(CopyWidthPixels);
	private ITimer? _copiedTimer;
	private bool _copied;
	private bool _disposed;

	/// <summary>Gets or sets the displayed repository location to copy.</summary>
	[Parameter]
	public required string Text { get; set; }

	/// <summary>Gets or sets the accessible name that identifies which location is copied.</summary>
	[Parameter]
	public required string Label { get; set; }

	/// <summary>Gets or sets the JavaScript runtime that reaches the clipboard, supplied by the framework.</summary>
	[Inject]
	public required IJSRuntime Js { get; set; }

	/// <summary>Gets or sets the clock the Copied confirmation runs on, supplied by the framework.</summary>
	[Inject]
	public required TimeProvider Time { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_copiedTimer?.Dispose();
		_cts.Cancel();
		_cts.Dispose();
	}

	private async Task CopyAsync()
	{
		try
		{
			await Js.InvokeVoidAsync("navigator.clipboard.writeText", _cts.Token, Text);
		}
		catch (Exception exception) when (exception is JSException or JSDisconnectedException)
		{
			// The browser has no clipboard to give (a page that is not served securely, or a refused permission) or the circuit is gone. Nothing is lost: the location stays on screen to select.
			return;
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
			return;
		}

		if (_disposed)
		{
			return;
		}

		_copied = true;
		_copiedTimer?.Dispose();
		_copiedTimer = Time.CreateTimer(OnCopiedExpired, null, s_copiedFor, Timeout.InfiniteTimeSpan);
	}

	// Raised on a thread of the clock's own: the work moves onto the component's context.
	private void OnCopiedExpired(object? state)
	{
		_ = InvokeAsync(ExpireCopied);
	}

	// Owns the work started by the timer: it only clears a flag, so there is nothing to lose with the task.
	private void ExpireCopied()
	{
		if (_disposed)
		{
			return;
		}

		_copied = false;
		StateHasChanged();
	}
}
