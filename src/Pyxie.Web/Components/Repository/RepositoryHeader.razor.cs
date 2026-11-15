using Blazorise;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Pyxie.Runtime.Git;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>The open project's repository at a glance: the project's name, its target branch, where it was imported from, and its hub's path to copy. Closing the project is on its row in the projects panel.</summary>
public partial class RepositoryHeader : ComponentBase, IDisposable
{
	private const int CopyWidthPixels = 92;

	private static readonly TimeSpan s_copiedFor = TimeSpan.FromSeconds(2);

	private readonly CancellationTokenSource _cts = new();

	// The Copy button keeps its width while it says Copied, so the line does not move.
	private readonly IFluentSizingStyle _copyWidth = Width.Px().Min(CopyWidthPixels);
	private ITimer? _copiedTimer;
	private bool _copied;
	private bool _disposed;

	/// <summary>Gets or sets the open project's name, which is what the page is about.</summary>
	[Parameter]
	public required string ProjectName { get; set; }

	/// <summary>Gets or sets the open project's repository.</summary>
	[Parameter]
	public required RepositoryEntity Repository { get; set; }

	/// <summary>Gets or sets the JavaScript runtime that reaches the clipboard, supplied by the framework.</summary>
	[Inject]
	public required IJSRuntime Js { get; set; }

	/// <summary>Gets or sets the clock the Copied confirmation runs on, supplied by the framework.</summary>
	[Inject]
	public required TimeProvider Time { get; set; }

	// Where the project was imported from, as recorded at import: the hub itself keeps no way back to it.
	private string OriginText => GitUrl.WithoutCredentials(Repository.Origin);

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
			// The hub's path alone, since people use it for more than cloning: adding it as a remote, pushing to it, opening it.
			await Js.InvokeVoidAsync("navigator.clipboard.writeText", _cts.Token, Repository.Path);
		}
		catch (Exception exception) when (exception is JSException or JSDisconnectedException)
		{
			// The browser has no clipboard to give (a page that is not served securely, or a refused permission) or the circuit is gone. Nothing is lost: the path stays on screen to select.
			return;
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
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
