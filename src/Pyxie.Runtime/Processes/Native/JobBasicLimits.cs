using System.Runtime.InteropServices;

namespace Pyxie.Runtime.Processes.Native;

/// <summary>The Windows <c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JobBasicLimits
{
	/// <summary>The per-process user-mode time limit.</summary>
	public long PerProcessUserTimeLimit;

	/// <summary>The per-job user-mode time limit.</summary>
	public long PerJobUserTimeLimit;

	/// <summary>The limits in effect, such as kill-on-close.</summary>
	public uint LimitFlags;

	/// <summary>The minimum working set size.</summary>
	public nuint MinimumWorkingSetSize;

	/// <summary>The maximum working set size.</summary>
	public nuint MaximumWorkingSetSize;

	/// <summary>The active process limit.</summary>
	public uint ActiveProcessLimit;

	/// <summary>The processor affinity.</summary>
	public nuint Affinity;

	/// <summary>The priority class.</summary>
	public uint PriorityClass;

	/// <summary>The scheduling class.</summary>
	public uint SchedulingClass;
}
