using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Media;

/// <summary>Hands immutable input snapshots into sandboxes and takes explicit publications into durable project storage.</summary>
public sealed class RoundMedia
{
	private const int MaxManifestBytes = 128 * 1024;
	private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
	private readonly IProjectDbContextSource _contexts;
	private readonly MediaStorage _storage;

	/// <summary>Initializes media exchange.</summary>
	/// <param name="contexts">Creates pinned project contexts.</param>
	/// <param name="storage">The private object store.</param>
	public RoundMedia(IProjectDbContextSource contexts, MediaStorage storage)
	{
		_contexts = contexts;
		_storage = storage;
	}

	/// <summary>Copies and verifies the exact input snapshot recorded when the round opened.</summary>
	/// <param name="project">The captured project.</param>
	/// <param name="roundId">The receiving round.</param>
	/// <param name="sandbox">The round's sandbox.</param>
	/// <param name="cancellationToken">A token to cancel copying.</param>
	/// <returns>A task that completes once the read-only input manifest is ready.</returns>
	public async Task HandInAsync(Project project, int roundId, ISandbox sandbox, CancellationToken cancellationToken)
	{
		await using ProjectDbContext context = _contexts.CreateFor(project);
		List<MediaFile> files = await context.RoundMediaInputs.AsNoTracking().Where(input => input.RoundId == roundId).Select(input => input.File).ToListAsync(cancellationToken);
		string root = Path.Combine(sandbox.InputFolder, "media");
		Directory.CreateDirectory(Path.Combine(root, "files"));
		List<MediaManifestEntry> entries = [];
		foreach (MediaFile file in files)
		{
			string relative = $"files/{file.Id:N}";
			try
			{
				await using FileStream source = MediaSource.OpenRegular(MediaPaths.Object(project.Folder, file.Id));
				if (source.Length != file.Length || Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)) != file.Sha256)
				{
					throw new IOException("The stored content changed.");
				}
				source.Position = 0;
				await using FileStream destination = new(Path.Combine(root, "files", file.Id.ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
				await source.CopyToAsync(destination, cancellationToken);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				throw new SandboxException($"The card's media '{file.Name}' is unavailable or changed. Restore it or remove it from the card's context.");
			}
			entries.Add(new MediaManifestEntry { Path = relative, Name = file.Name, Caption = file.Caption, Length = file.Length, Sha256 = file.Sha256, Source = file.RoundId is int producer ? $"Agent output from round {producer}" : "User context" });
		}
		await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), JsonSerializer.Serialize(new MediaManifest { Files = entries }, s_json), cancellationToken);
		Directory.CreateDirectory(Path.Combine(sandbox.OutputFolder, "media", "files"));
	}

	/// <summary>Collects a quiescent output directory. File problems are recorded independently of the round's result.</summary>
	/// <param name="project">The captured project.</param>
	/// <param name="cardId">The producing card.</param>
	/// <param name="roundId">The producing round.</param>
	/// <param name="outputFolder">The sandbox's host output folder.</param>
	/// <param name="commit">The round commit observed by Pyxie, or null.</param>
	/// <param name="cancellationToken">A token bounding collection.</param>
	/// <returns>Durable files and any collection problems.</returns>
	public async Task<MediaCollection> CollectAsync(Project project, int cardId, int roundId, string outputFolder, string? commit, CancellationToken cancellationToken)
	{
		using IDisposable held = await _storage.AcquireAsync(cancellationToken);
		MediaCollection recovered = await ReadReceiptAsync(project.Folder, roundId, cancellationToken);
		if (recovered.Complete)
		{
			return recovered;
		}
		string root = Path.Combine(outputFolder, "media");
		string manifestPath = Path.Combine(root, "manifest.json");
		if (!File.Exists(manifestPath))
		{
			return recovered;
		}
		List<MediaFile> files = [.. recovered.Files];
		List<string> problems = [.. recovered.Problems];
		try
		{
			MediaSource.CheckPath(outputFolder);
			MediaSource.CheckPath(root);
			await using FileStream source = MediaSource.OpenRegular(manifestPath);
			if (source.Length > MaxManifestBytes)
			{
				throw new IOException("The media manifest exceeds 128 KiB.");
			}
			MediaManifest? manifest = await JsonSerializer.DeserializeAsync<MediaManifest>(source, s_json, cancellationToken);
			if (manifest is null || manifest.Version != 1 || manifest.Files is null || manifest.Files.Count > MediaLimits.MaxFiles)
			{
				throw new IOException("The media manifest must use version 1 and contain at most 50 files.");
			}
			HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
			foreach (MediaManifestEntry entry in manifest.Files)
			{
				try
				{
					if (entry is null || entry.Path is null || entry.Path.Length > 512 || !paths.Add(entry.Path) || entry.Caption?.Length > MediaLimits.MaxCaptionLength)
					{
						throw new IOException("A media entry has a duplicate path or invalid metadata.");
					}
					if (files.Any(file => file.PublicationKey == entry.Path))
					{
						continue;
					}
					await using FileStream content = MediaSource.Open(root, entry.Path);
					ServiceResult<MediaFile> stored = await _storage.WriteAsync(project.Folder, entry.Name, content, _storage.Limits.MaxBatchBytes - files.Sum(file => file.Length), cancellationToken);
					if (stored.Value is not MediaFile file)
					{
						problems.Add(stored.Error ?? "A media file could not be stored.");
						continue;
					}
					file.CardId = cardId;
					file.RoundId = roundId;
					file.PublicationKey = entry.Path;
					file.Caption = entry.Caption;
					file.Commit = commit;
					files.Add(file);
					await WriteReceiptAsync(project.Folder, roundId, new MediaCollection(files, problems), cancellationToken);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
				{
					problems.Add($"A media file could not be attached: {exception.Message}");
				}
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			problems.Add($"Media could not be collected: {exception.Message}");
		}
		MediaCollection result = new(files, problems, Complete: true);
		await WriteReceiptAsync(project.Folder, roundId, result, cancellationToken);
		return result;
	}

	/// <summary>Reads a host-owned recovery receipt without consulting the disposable sandbox.</summary>
	/// <param name="projectFolder">The owning project folder.</param>
	/// <param name="roundId">The producing round.</param>
	/// <param name="cancellationToken">A token to cancel reading.</param>
	/// <returns>The recorded collection, or an empty collection when none exists.</returns>
	public static async Task<MediaCollection> ReadReceiptAsync(string projectFolder, int roundId, CancellationToken cancellationToken)
	{
		string path = MediaPaths.Receipt(projectFolder, roundId);
		if (!File.Exists(path))
		{
			return MediaCollection.Empty;
		}
		await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		return await JsonSerializer.DeserializeAsync<MediaCollection>(stream, s_json, cancellationToken) ?? MediaCollection.Empty;
	}

	private static async Task WriteReceiptAsync(string projectFolder, int roundId, MediaCollection collection, CancellationToken cancellationToken)
	{
		string path = MediaPaths.Receipt(projectFolder, roundId);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException("A receipt has a parent folder."));
		string temporary = path + ".part";
		await using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			await JsonSerializer.SerializeAsync(stream, collection, s_json, cancellationToken);
			await stream.FlushAsync(cancellationToken);
			stream.Flush(flushToDisk: true);
		}
		File.Move(temporary, path, overwrite: true);
	}
}
