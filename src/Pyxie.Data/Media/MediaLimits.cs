namespace Pyxie.Data.Media;

/// <summary>Bounds of the persisted media contract. Runtime configuration can impose smaller byte budgets.</summary>
public static class MediaLimits
{
	/// <summary>The largest context or output set.</summary>
	public const int MaxFiles = 50;
	/// <summary>The longest display filename.</summary>
	public const int MaxNameLength = 240;
	/// <summary>The longest plain-text caption.</summary>
	public const int MaxCaptionLength = 1000;
	/// <summary>The largest file in bytes.</summary>
	public const long MaxFileBytes = 256L * 1024 * 1024;
	/// <summary>The largest upload or output batch in bytes.</summary>
	public const long MaxBatchBytes = 1024L * 1024 * 1024;
	/// <summary>How long an unsaved upload remains usable.</summary>
	public static readonly TimeSpan UploadLifetime = TimeSpan.FromHours(24);
}
