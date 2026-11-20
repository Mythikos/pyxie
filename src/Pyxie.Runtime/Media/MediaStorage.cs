using System.Security.Cryptography;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Media;

/// <summary>Owns immutable media files. Writes and collection are serialized with removal and quota checks; uploads are cancelled when their project closes.</summary>
public sealed class MediaStorage : IDisposable
{
	private readonly SemaphoreSlim _writes = new(1, 1);
	private readonly object _changeLock = new();
	private readonly Dictionary<Guid, MediaUploadReservation> _uploads = [];
	private readonly OpenProject _openProject;
	private readonly IProjectDbContextSource _contexts;
	private readonly TimeProvider _clock;
	private readonly MediaOptions _options;
	private CancellationTokenSource _projectChanged = new();

	/// <summary>Initializes the project media store.</summary>
	/// <param name="openProject">The current project and its change notification.</param>
	/// <param name="contexts">Creates pinned project contexts.</param>
	/// <param name="clock">The clock for file timestamps and expiration.</param>
	/// <param name="options">The storage budgets.</param>
	public MediaStorage(OpenProject openProject, IProjectDbContextSource contexts, TimeProvider clock, IOptions<MediaOptions> options)
	{
		_openProject = openProject;
		_contexts = contexts;
		_clock = clock;
		_options = options.Value;
		_openProject.Changed += OnProjectChanged;
	}

	/// <summary>Gets the configured budgets.</summary>
	public MediaOptions Limits => _options;

	/// <summary>Serializes storage changes, including deleting a project. The caller must dispose the handle.</summary>
	/// <param name="cancellationToken">A token to cancel waiting.</param>
	/// <returns>The held storage lock.</returns>
	public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
	{
		await _writes.WaitAsync(cancellationToken);
		return new RepositoryLockRelease(_writes);
	}

	/// <summary>Streams an upload into immutable storage and records it in its editor session.</summary>
	/// <param name="project">The captured project.</param>
	/// <param name="uploadId">The editor session.</param>
	/// <param name="name">The display filename.</param>
	/// <param name="content">The untrusted input stream.</param>
	/// <param name="cancellationToken">A token to cancel the upload.</param>
	/// <returns>The staged file or an actionable refusal.</returns>
	public async Task<ServiceResult<MediaFile>> UploadAsync(Project project, Guid uploadId, string name, Stream content, CancellationToken cancellationToken)
	{
		using CancellationTokenSource linked = LinkProjectChange(cancellationToken);
		CancellationToken token = linked.Token;
		MediaUploadReservation reservation;
		using (await AcquireAsync(token))
		{
			if (_openProject.Current?.Id != project.Id || !Directory.Exists(project.Folder))
			{
				return ServiceResult<MediaFile>.Failure("The project is no longer open.");
			}
			if (!MediaContent.ValidName(name))
			{
				return ServiceResult<MediaFile>.Failure("The file name is invalid or too long.");
			}
			await using ProjectDbContext context = _contexts.CreateFor(project);
			if (await UploadProblemAsync(context, uploadId, token) is string problem)
			{
				return ServiceResult<MediaFile>.Failure(problem);
			}
			List<long> batch = await context.MediaFiles.Where(file => file.UploadId == uploadId).Select(file => file.Length).ToListAsync(token);
			MediaUploadReservation[] pending = [.. _uploads.Values.Where(item => item.UploadId == uploadId && item.ProjectFolder == project.Folder)];
			if (batch.Count + pending.Length >= Data.Media.MediaLimits.MaxFiles)
			{
				return ServiceResult<MediaFile>.Failure("An upload can contain at most 50 files.");
			}
			long limit = Math.Min(_options.MaxFileBytes, Math.Min(_options.MaxBatchBytes - batch.Sum() - pending.Sum(item => item.Limit), AvailableBytes(project.Folder)));
			if (limit < 0)
			{
				return ServiceResult<MediaFile>.Failure("The project's media storage is full.");
			}
			reservation = new MediaUploadReservation(Guid.NewGuid(), project.Folder, uploadId, limit);
			Directory.CreateDirectory(Path.Combine(MediaPaths.Root(project.Folder), "staging"));
			_uploads.Add(reservation.Id, reservation);
		}

		// Receiving browser bytes holds no storage lock. A slow connection cannot block round publication or cleanup; its reserved budget still counts toward quotas.
		try
		{
			ServiceResult<MediaFile> received = await ReceiveAsync(project.Folder, reservation.Id, name, content, reservation.Limit, token);
			if (received.Value is not MediaFile file)
			{
				return received;
			}
			using IDisposable held = await AcquireAsync(token);
			await using ProjectDbContext context = _contexts.CreateFor(project);
			if (await UploadProblemAsync(context, uploadId, token) is string problem)
			{
				return ServiceResult<MediaFile>.Failure(problem);
			}
			Directory.CreateDirectory(Path.Combine(MediaPaths.Root(project.Folder), "objects"));
			File.Move(TemporaryPath(project.Folder, file.Id), MediaPaths.Object(project.Folder, file.Id));
			file.UploadId = uploadId;
			try
			{
				context.MediaFiles.Add(file);
				await context.SaveChangesAsync(token);
				return ServiceResult<MediaFile>.Success(file);
			}
			catch
			{
				File.Delete(MediaPaths.Object(project.Folder, file.Id));
				throw;
			}
		}
		catch (DbUpdateException)
		{
			return ServiceResult<MediaFile>.Failure("The upload session changed or closed. Reopen the card.");
		}
		finally
		{
			using IDisposable held = await AcquireAsync(CancellationToken.None);
			_uploads.Remove(reservation.Id);
			try
			{
				File.Delete(TemporaryPath(project.Folder, reservation.Id));
			}
			finally
			{
				reservation.Finished.TrySetResult();
			}
		}
	}

	/// <summary>Waits for cancelled uploads of a closed project before its folder is removed.</summary>
	/// <param name="projectFolder">The closed project's folder.</param>
	/// <param name="cancellationToken">A token to cancel waiting.</param>
	/// <returns>A task completing after the uploads release their files.</returns>
	public async Task DrainUploadsAsync(string projectFolder, CancellationToken cancellationToken)
	{
		Task[] pending;
		using (await AcquireAsync(cancellationToken))
		{
			pending = [.. _uploads.Values.Where(item => item.ProjectFolder == projectFolder).Select(item => item.Finished.Task)];
		}
		await Task.WhenAll(pending).WaitAsync(cancellationToken);
	}

	internal bool IsReceiving(string path) => _uploads.Values.Any(item => TemporaryPath(item.ProjectFolder, item.Id) == path);

	private async Task<string?> UploadProblemAsync(ProjectDbContext context, Guid uploadId, CancellationToken token)
	{
		MediaUpload? upload = await context.MediaUploads.AsNoTracking().FirstOrDefaultAsync(item => item.Id == uploadId, token);
		if (upload is null || upload.ExpiresUtc <= _clock.GetUtcNow().UtcDateTime)
		{
			return "The upload session expired or closed. Reopen the card.";
		}
		return upload.CardId is int cardId && !await context.Cards.AnyAsync(card => card.Id == cardId && card.Lane == Data.Enums.CardLane.Queue && card.Version == upload.CardVersion, token) ? "The card changed. Reopen it before attaching files." : null;
	}

	private long AvailableBytes(string projectFolder)
	{
		string objects = Path.Combine(MediaPaths.Root(projectFolder), "objects");
		long used = Directory.Exists(objects) ? Directory.EnumerateFiles(objects).Sum(path => new FileInfo(path).Length) : 0;
		return _options.MaxProjectBytes - used - _uploads.Values.Where(item => item.ProjectFolder == projectFolder).Sum(item => item.Limit);
	}

	private static string TemporaryPath(string folder, Guid id) => Path.Combine(MediaPaths.Root(folder), "staging", $"{id:N}.part");

	/// <summary>Copies bounded bytes into a new object. Callers hold the storage lock and record its metadata or a recovery receipt before releasing it.</summary>
	/// <param name="projectFolder">The captured project folder.</param>
	/// <param name="name">The display filename.</param>
	/// <param name="content">The input stream.</param>
	/// <param name="limit">The remaining batch budget.</param>
	/// <param name="cancellationToken">A token to cancel copying.</param>
	/// <returns>The durable object or a refusal.</returns>
	public async Task<ServiceResult<MediaFile>> WriteAsync(string projectFolder, string name, Stream content, long limit, CancellationToken cancellationToken)
	{
		if (!Directory.Exists(projectFolder))
		{
			return ServiceResult<MediaFile>.Failure("The project folder is no longer available.");
		}
		if (!MediaContent.ValidName(name))
		{
			return ServiceResult<MediaFile>.Failure("The file name is invalid or too long.");
		}
		string root = MediaPaths.Root(projectFolder);
		string objects = Path.Combine(root, "objects");
		string staging = Path.Combine(root, "staging");
		Directory.CreateDirectory(objects);
		Directory.CreateDirectory(staging);
		long allowed = Math.Min(Math.Min(limit, _options.MaxFileBytes), AvailableBytes(projectFolder));
		if (allowed < 0)
		{
			return ServiceResult<MediaFile>.Failure("The project's media storage is full.");
		}

		Guid id = Guid.NewGuid();
		string temporary = TemporaryPath(projectFolder, id);
		try
		{
			ServiceResult<MediaFile> result = await ReceiveAsync(projectFolder, id, name, content, allowed, cancellationToken);
			if (result.Succeeded)
			{
				File.Move(temporary, MediaPaths.Object(projectFolder, id));
			}
			return result;
		}
		finally
		{
			File.Delete(temporary);
		}
	}

	private async Task<ServiceResult<MediaFile>> ReceiveAsync(string projectFolder, Guid id, string name, Stream content, long allowed, CancellationToken cancellationToken)
	{
		string temporary = TemporaryPath(projectFolder, id);
		try
		{
			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			byte[] buffer = new byte[64 * 1024];
			byte[] header = new byte[512];
			int headerLength = 0;
			long length = 0;
			await using (FileStream destination = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous))
			{
				while (true)
				{
					int read = await content.ReadAsync(buffer, cancellationToken);
					if (read == 0)
					{
						break;
					}
					length += read;
					if (length > allowed)
					{
						return ServiceResult<MediaFile>.Failure("The file exceeds the file, batch, or project media limit.");
					}
					int take = Math.Min(read, header.Length - headerLength);
					buffer.AsSpan(0, take).CopyTo(header.AsSpan(headerLength));
					headerLength += take;
					hash.AppendData(buffer, 0, read);
					await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}
				await destination.FlushAsync(cancellationToken);
				destination.Flush(flushToDisk: true);
			}
			return ServiceResult<MediaFile>.Success(new MediaFile { Id = id, Name = name, ContentType = MediaContent.Detect(header.AsSpan(0, headerLength)), Length = length, Sha256 = Convert.ToHexString(hash.GetHashAndReset()), CreatedUtc = _clock.GetUtcNow().UtcDateTime });
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return ServiceResult<MediaFile>.Failure("The file could not be stored. Check the project's disk space and folder permissions.");
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_openProject.Changed -= OnProjectChanged;
		_projectChanged.Cancel();
		_projectChanged.Dispose();
		_writes.Dispose();
	}

	private void OnProjectChanged()
	{
		CancellationTokenSource previous;
		lock (_changeLock)
		{
			previous = _projectChanged;
			_projectChanged = new CancellationTokenSource();
		}
		previous.Cancel();
		previous.Dispose();
	}

	private CancellationTokenSource LinkProjectChange(CancellationToken cancellationToken)
	{
		lock (_changeLock)
		{
			return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _projectChanged.Token);
		}
	}
}
