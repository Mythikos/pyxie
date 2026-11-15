using System.Collections.Concurrent;
using System.Globalization;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>A stand-in for <see cref="DockerSandboxes"/> in tests (see <see cref="LocalSandbox"/>). Each sandbox works in a folder under the stand-in's own folder named after the sandbox; a worker's folder stands for its volumes and stays until the card is removed. It records every sandbox it starts, so a test can see what each was given.</summary>
public sealed class LocalSandboxes : ISandboxes
{
	/// <summary>The image tag the stand-in hands out.</summary>
	public const string Image = "local-stand-in";

	private readonly ProcessRunner _runner;
	private readonly string _folder;
	private readonly ConcurrentQueue<SandboxSpec> _started = new();

	/// <summary>Initializes a new instance of the <see cref="LocalSandboxes"/> class.</summary>
	/// <param name="runner">The process runner commands run through.</param>
	/// <param name="folder">The folder the sandboxes work in, created when the first one starts; the caller deletes it.</param>
	public LocalSandboxes(ProcessRunner runner, string folder)
	{
		_runner = runner;
		_folder = folder;
	}

	/// <summary>Gets the specs of every sandbox started, in order.</summary>
	public IReadOnlyList<SandboxSpec> Started => [.. _started];

	/// <summary>Gets the folder that stands for a sandbox's working folder.</summary>
	/// <param name="name">The sandbox's name.</param>
	/// <returns>The full path.</returns>
	public string WorkFolder(string name)
	{
		return Path.Combine(_folder, name, "work");
	}

	/// <summary>Deletes a folder, also when git left read-only files in it.</summary>
	/// <param name="folder">The folder.</param>
	public static void DeleteFolder(string folder)
	{
		if (!Directory.Exists(folder))
		{
			return;
		}

		foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(folder, recursive: true);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult.Success());
	}

	/// <inheritdoc/>
	public Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		return Task.FromResult(ServiceResult<string>.Success(Image));
	}

	/// <inheritdoc/>
	public Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(spec.InputFolder);
		Directory.CreateDirectory(spec.OutputFolder);

		string work = WorkFolder(spec.Name);
		if (!spec.Keep)
		{
			DeleteFolder(work);
		}

		Directory.CreateDirectory(work);
		_started.Enqueue(spec);

		return Task.FromResult<ISandbox>(new LocalSandbox(_runner, spec, work));
	}

	/// <inheritdoc/>
	public Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		foreach (string folder in CardFolders(projectKey).Where(entry => entry.CardId == cardId).Select(entry => entry.Folder))
		{
			DeleteFolder(folder);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		foreach (string folder in CardFolders(projectKey).Where(entry => !entry.Folder.EndsWith($"-{SandboxNames.RoleName(SandboxRole.Worker)}", StringComparison.Ordinal)).Select(entry => entry.Folder))
		{
			DeleteFolder(folder);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlySet<int>>(CardFolders(projectKey).Select(entry => entry.CardId).ToHashSet());
	}

	// Sandbox folders are named "pyxie-<key>-card-<id>-<role>".
	private IEnumerable<(string Folder, int CardId)> CardFolders(string projectKey)
	{
		string prefix = $"pyxie-{projectKey}-card-";
		if (!Directory.Exists(_folder))
		{
			yield break;
		}

		foreach (string folder in Directory.EnumerateDirectories(_folder))
		{
			string name = Path.GetFileName(folder);
			if (!name.StartsWith(prefix, StringComparison.Ordinal))
			{
				continue;
			}

			string rest = name[prefix.Length..];
			int dash = rest.IndexOf('-', StringComparison.Ordinal);
			if (dash > 0 && int.TryParse(rest[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out int cardId))
			{
				yield return (folder, cardId);
			}
		}
	}
}
