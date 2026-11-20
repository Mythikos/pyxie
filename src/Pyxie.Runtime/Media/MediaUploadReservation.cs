namespace Pyxie.Runtime.Media;

/// <summary>A bounded in-flight HTTP upload. Access is protected by the storage lock.</summary>
internal sealed record MediaUploadReservation(Guid Id, string ProjectFolder, Guid UploadId, long Limit)
{
	internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
