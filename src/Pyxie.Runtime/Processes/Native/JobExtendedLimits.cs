using System.Runtime.InteropServices;

namespace Pyxie.Runtime.Processes.Native;

/// <summary>The Windows <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JobExtendedLimits
{
	/// <summary>The basic limits.</summary>
	public JobBasicLimits Basic;

	/// <summary>Reserved.</summary>
	public JobIoCounters IoInfo;

	/// <summary>The per-process memory limit.</summary>
	public nuint ProcessMemoryLimit;

	/// <summary>The job memory limit.</summary>
	public nuint JobMemoryLimit;

	/// <summary>The peak process memory used.</summary>
	public nuint PeakProcessMemoryUsed;

	/// <summary>The peak job memory used.</summary>
	public nuint PeakJobMemoryUsed;
}
