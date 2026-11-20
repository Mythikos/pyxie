namespace Pyxie.Tests.Web.Rendering;

/// <summary>The stream of a picked file that cannot be read to its end: every read fails with the exception it was made with, such as the <see cref="InvalidOperationException"/> Blazor's stream from the browser fails with when the browser cannot read the file, or the <see cref="EndOfStreamException"/> it fails with when what arrives breaks the stream's rules.</summary>
internal sealed class InterruptedUploadStream : Stream
{
	private readonly Exception _failure;

	/// <summary>Initializes a new instance of the <see cref="InterruptedUploadStream"/> class.</summary>
	/// <param name="failure">The exception every read fails with.</param>
	public InterruptedUploadStream(Exception failure)
	{
		_failure = failure;
	}

	/// <inheritdoc/>
	public override bool CanRead => true;

	/// <inheritdoc/>
	public override bool CanSeek => false;

	/// <inheritdoc/>
	public override bool CanWrite => false;

	/// <inheritdoc/>
	public override long Length => throw new NotSupportedException();

	/// <inheritdoc/>
	public override long Position
	{
		get => throw new NotSupportedException();
		set => throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override int Read(byte[] buffer, int offset, int count)
	{
		throw Interrupted();
	}

	/// <inheritdoc/>
	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return Task.FromException<int>(Interrupted());
	}

	/// <inheritdoc/>
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromException<int>(Interrupted());
	}

	/// <inheritdoc/>
	public override void Flush()
	{
	}

	/// <inheritdoc/>
	public override long Seek(long offset, SeekOrigin origin)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override void SetLength(long value)
	{
		throw new NotSupportedException();
	}

	/// <inheritdoc/>
	public override void Write(byte[] buffer, int offset, int count)
	{
		throw new NotSupportedException();
	}

	private Exception Interrupted()
	{
		return _failure;
	}
}
