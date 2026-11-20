using System.Collections.Concurrent;
using System.Globalization;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>A stand-in for <see cref="DockerSandboxes"/> in tests (see <see cref="LocalSandbox"/>). Each sandbox works in a folder under the stand-in's own folder named after the sandbox; a worker's folder stands for its volumes and stays until the card is removed. It records every sandbox it starts, so a test can see what each was given. It remembers the images it handed out for a project's Dockerfile, which <see cref="FindImageAsync"/> finds, a call to <see cref="RemoveUnusedImagesAsync"/> that ran removes except for the one it is told to keep, and removing the project removes with its sandboxes' folders.</summary>
public sealed class LocalSandboxes : ISandboxes
{
	/// <summary>The image tag the stand-in hands out.</summary>
	public const string Image = "local-stand-in";

	private readonly ProcessRunner _runner;
	private readonly string _folder;
	private readonly ConcurrentQueue<SandboxSpec> _started = new();
	private readonly ConcurrentQueue<(string ProjectKey, string Dockerfile)> _unusedImageRemovals = new();
	private readonly ConcurrentDictionary<(string ProjectKey, string Dockerfile), string> _images = new();
	private readonly ConcurrentQueue<string> _projectRemovals = new();
	private volatile bool _removeUnusedImagesResult = true;
	private volatile Exception? _removeUnusedImagesFailure;
	private volatile Exception? _removeProjectFailure;
	private volatile Func<SandboxImageRequest, CancellationToken, Task<ServiceResult<string>>>? _ensureImage;

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

	/// <summary>Gets every call to <see cref="RemoveUnusedImagesAsync"/>, in order: the project's key and the Dockerfile whose image was to stay.</summary>
	public IReadOnlyList<(string ProjectKey, string Dockerfile)> UnusedImageRemovals => [.. _unusedImageRemovals];

	/// <summary>Gets or sets what <see cref="RemoveUnusedImagesAsync"/> returns: <see langword="true"/> as when it ran, <see langword="false"/> as when a build was running so it did nothing.</summary>
	public bool RemoveUnusedImagesResult
	{
		get => _removeUnusedImagesResult;
		set => _removeUnusedImagesResult = value;
	}

	/// <summary>Gets or sets what <see cref="RemoveUnusedImagesAsync"/> throws after recording the call, such as a <see cref="SandboxUnavailableException"/> for a container engine that does not answer; <see langword="null"/> while it works.</summary>
	public Exception? RemoveUnusedImagesFailure
	{
		get => _removeUnusedImagesFailure;
		set => _removeUnusedImagesFailure = value;
	}

	/// <summary>Gets the keys of the projects <see cref="RemoveProjectAsync"/> was asked to remove, in order, also when it failed.</summary>
	public IReadOnlyList<string> ProjectRemovals => [.. _projectRemovals];

	/// <summary>Gets or sets what <see cref="RemoveProjectAsync"/> throws after recording the call, instead of removing anything, such as a <see cref="SandboxUnavailableException"/> for a container engine that does not answer; <see langword="null"/> while it works.</summary>
	public Exception? RemoveProjectFailure
	{
		get => _removeProjectFailure;
		set => _removeProjectFailure = value;
	}

	/// <summary>Gets or sets what <see cref="EnsureImageAsync"/> does instead of handing out <see cref="Image"/> at once, so a test can make an image build fail, wait, or report itself through <see cref="SandboxImageRequest.BuildStarting"/>; <see langword="null"/> to hand out <see cref="Image"/>.</summary>
	public Func<SandboxImageRequest, CancellationToken, Task<ServiceResult<string>>>? EnsureImage
	{
		get => _ensureImage;
		set => _ensureImage = value;
	}

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
	public async Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		ServiceResult<string> result = _ensureImage is { } ensureImage ? await ensureImage(request, cancellationToken) : ServiceResult<string>.Success(Image);
		if (result.Succeeded && result.Value is string tag)
		{
			_images[(request.ProjectKey, Normalized(request.Dockerfile))] = tag;
		}

		return result;
	}

	/// <inheritdoc/>
	public Task<string?> FindImageAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		return Task.FromResult(_images.GetValueOrDefault((projectKey, Normalized(dockerfile))));
	}

	/// <inheritdoc/>
	public Task<bool> RemoveUnusedImagesAsync(string projectKey, string dockerfile, CancellationToken cancellationToken)
	{
		// What the call answers is read before it is recorded, so a test that sees the call and then changes the answer never changes this call's.
		Exception? failure = _removeUnusedImagesFailure;
		bool result = _removeUnusedImagesResult;
		_unusedImageRemovals.Enqueue((projectKey, dockerfile));
		if (failure is null && result)
		{
			string kept = Normalized(dockerfile);
			foreach ((string ProjectKey, string Dockerfile) image in _images.Keys.Where(image => image.ProjectKey == projectKey && image.Dockerfile != kept))
			{
				_images.TryRemove(image, out _);
			}
		}

		return failure is null ? Task.FromResult(result) : Task.FromException<bool>(failure);
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
	public Task QuiesceCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
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
	public Task RemoveProjectAsync(string projectKey, CancellationToken cancellationToken)
	{
		Exception? failure = _removeProjectFailure;
		_projectRemovals.Enqueue(projectKey);
		if (failure is not null)
		{
			return Task.FromException(failure);
		}

		foreach (string folder in CardFolders(projectKey).Select(entry => entry.Folder))
		{
			DeleteFolder(folder);
		}

		foreach ((string ProjectKey, string Dockerfile) image in _images.Keys.Where(image => image.ProjectKey == projectKey))
		{
			_images.TryRemove(image, out _);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		return Task.FromResult<IReadOnlySet<int>>(CardFolders(projectKey).Select(entry => entry.CardId).ToHashSet());
	}

	// The engine tags an image by its Dockerfile with the line endings it builds, so two spellings of the same lines are one image.
	private static string Normalized(string dockerfile)
	{
		return dockerfile.ReplaceLineEndings("\n");
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
