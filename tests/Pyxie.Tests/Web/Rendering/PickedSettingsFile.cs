using Blazorise;

using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A file a person picked in the browser's file picker, as Blazorise reports it to the server: the size the browser reported and a stream of its content. It records each time it is opened and the most it was opened to read, so a test can tell a file that was refused unread from one that was read.</summary>
internal sealed class PickedSettingsFile : IFileEntry
{
	private readonly Func<Stream> _open;
	private int _opens;
	private long _maxAllowedSize = -1;

	/// <summary>Initializes a new instance of the <see cref="PickedSettingsFile"/> class whose content is read from a stream.</summary>
	/// <param name="open">Opens the stream of the file's content, or throws as the browser that stopped sending it does.</param>
	/// <param name="size">The size the browser reports, in bytes.</param>
	public PickedSettingsFile(Func<Stream> open, long size)
	{
		_open = open;
		Size = size;
	}

	/// <summary>Initializes a new instance of the <see cref="PickedSettingsFile"/> class with its content.</summary>
	/// <param name="content">The file's content, whose length is the size the browser reports.</param>
	public PickedSettingsFile(byte[] content)
		: this(() => new MemoryStream(content, writable: false), content.Length)
	{
	}

	/// <summary>Gets how many times the file was opened for reading.</summary>
	public int Opens => Volatile.Read(ref _opens);

	/// <summary>Gets the most bytes the last opening allowed to be read, or -1 when the file was never opened.</summary>
	public long MaxAllowedSize => Interlocked.Read(ref _maxAllowedSize);

	/// <inheritdoc/>
	public int Id => 1;

	/// <inheritdoc/>
	public DateTime LastModified { get; } = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <inheritdoc/>
	public string Name => $"stand-in{SettingsFileFormat.FileNameSuffix}";

	/// <inheritdoc/>
	public string RelativePath => string.Empty;

	/// <inheritdoc/>
	public long Size { get; }

	/// <inheritdoc/>
	public string Type => SettingsFileFormat.MediaType;

	/// <inheritdoc/>
	public string UploadUrl { get; set; } = string.Empty;

	/// <inheritdoc/>
	public string ErrorMessage { get; set; } = string.Empty;

	/// <inheritdoc/>
	public FileEntryStatus Status { get; set; } = FileEntryStatus.Ready;

	/// <inheritdoc/>
	public TaskCompletionSource FileUploadEndedCallback { get; set; } = new();

	/// <summary>Creates the file Export would have downloaded for settings.</summary>
	/// <param name="settings">The settings the file holds.</param>
	/// <returns>The picked file.</returns>
	public static PickedSettingsFile Of(ProjectSettings settings)
	{
		return new PickedSettingsFile(SettingsFileWriter.Write(settings));
	}

	/// <inheritdoc/>
	public Stream OpenReadStream(long maxAllowedSize, CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _opens);
		Interlocked.Exchange(ref _maxAllowedSize, maxAllowedSize);

		return _open();
	}

	/// <inheritdoc/>
	public Task WriteToStreamAsync(Stream stream, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("A picked settings file is read through OpenReadStream, which holds the read to the size limit.");
	}

	/// <inheritdoc/>
	public Task Cancel()
	{
		return Task.CompletedTask;
	}
}
