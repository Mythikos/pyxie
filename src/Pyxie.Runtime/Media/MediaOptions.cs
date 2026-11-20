using Pyxie.Data.Media;

namespace Pyxie.Runtime.Media;

/// <summary>Machine-local media budgets, configured at the composition root.</summary>
public sealed class MediaOptions
{
	/// <summary>The configuration section.</summary>
	public const string SectionName = "Media";
	/// <summary>Gets or sets the maximum bytes accepted in one file.</summary>
	public long MaxFileBytes { get; set; } = MediaLimits.MaxFileBytes;
	/// <summary>Gets or sets the maximum bytes in one upload or output batch.</summary>
	public long MaxBatchBytes { get; set; } = MediaLimits.MaxBatchBytes;
	/// <summary>Gets or sets the total bytes retained in one project's media store.</summary>
	public long MaxProjectBytes { get; set; } = 10L * 1024 * 1024 * 1024;
}
