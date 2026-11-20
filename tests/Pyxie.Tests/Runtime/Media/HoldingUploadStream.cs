namespace Pyxie.Tests.Runtime.Media;

internal sealed class HoldingUploadStream : MemoryStream
{
	internal TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	internal TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	internal HoldingUploadStream() : base("bytes"u8.ToArray())
	{
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		Reading.TrySetResult();
		await Continue.Task.WaitAsync(cancellationToken);
		return await base.ReadAsync(buffer, cancellationToken);
	}
}
