using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Projects;

namespace Pyxie.Runtime.Repositories;

/// <summary>Runs project imports (see <see cref="ProjectSetupService"/>) in the background, one at a time, so an import or a clone that takes minutes outlives the page that started it: the person can go elsewhere and come back to find it running, failed with git's reason, or done. Only Pyxie shutting down cancels it.</summary>
public sealed class RepositoryImports : IDisposable
{
	private readonly IServiceScopeFactory _scopes;
	private readonly CancellationTokenSource _shutdown = new();
	private readonly Lock _gate = new();
	private RepositoryImport? _current;
	private Task _running = Task.CompletedTask;

	/// <summary>Initializes a new instance of the <see cref="RepositoryImports"/> class.</summary>
	/// <param name="scopes">Creates the scope each import runs in.</param>
	public RepositoryImports(IServiceScopeFactory scopes)
	{
		_scopes = scopes;
	}

	/// <summary>Raised when an import starts, fails, or finishes. Handlers run on whichever thread made the change and must not throw.</summary>
	public event Action? Changed;

	/// <summary>Gets the import that is running or that last failed, or <see langword="null"/> when there is neither (none started, or the last one succeeded).</summary>
	public RepositoryImport? Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	/// <summary>Gets the running import's task, which completes when it ends however it ends; a completed task when none runs.</summary>
	public Task Running
	{
		get
		{
			lock (_gate)
			{
				return _running;
			}
		}
	}

	/// <summary>Starts importing a folder or cloning a URL, unless an import is already running.</summary>
	/// <param name="source">Whether <paramref name="origin"/> is a folder or a URL.</param>
	/// <param name="origin">The folder or URL.</param>
	/// <returns><see langword="true"/> when the import started; <see langword="false"/> when one was already running.</returns>
	public bool TryStart(RepositorySource source, string origin)
	{
		lock (_gate)
		{
			if (_current is { IsRunning: true })
			{
				return false;
			}

			_current = new RepositoryImport(source, origin, null);
			CancellationToken shutdown = _shutdown.Token;
			_running = Task.Run(() => RunAsync(source, origin, shutdown), CancellationToken.None);
		}

		Changed?.Invoke();

		return true;
	}

	/// <summary>Forgets a failed import, so the page stops showing its error.</summary>
	public void Dismiss()
	{
		lock (_gate)
		{
			if (_current is not { IsRunning: false })
			{
				return;
			}

			_current = null;
		}

		Changed?.Invoke();
	}

	/// <summary>Cancels a running import, which then removes what it cloned.</summary>
	public void Dispose()
	{
		_shutdown.Cancel();
		_shutdown.Dispose();
	}

	// Owns the import from start to end: every outcome, an unexpected exception included, ends up in Current, so nothing is left unobserved.
	private async Task RunAsync(RepositorySource source, string origin, CancellationToken shutdown)
	{
		string? error;
		try
		{
			await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
			ProjectSetupService setup = scope.ServiceProvider.GetRequiredService<ProjectSetupService>();
			ServiceResult<Repository> result = source == RepositorySource.Folder
				? await setup.ImportFolderAsync(origin, shutdown)
				: await setup.CloneAsync(origin, shutdown);
			error = result.Succeeded ? null : result.Error ?? "The repository could not be imported.";
		}
		catch (OperationCanceledException)
		{
			error = "Pyxie shut down before the import finished.";
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			error = $"The import failed: {exception.Message}";
		}

		lock (_gate)
		{
			_current = error is null ? null : new RepositoryImport(source, origin, error);
		}

		Changed?.Invoke();
	}
}
