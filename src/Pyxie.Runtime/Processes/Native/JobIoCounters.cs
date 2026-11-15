using System.Runtime.InteropServices;

namespace Pyxie.Runtime.Processes.Native;

/// <summary>The Windows <c>IO_COUNTERS</c> structure.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct JobIoCounters
{
	/// <summary>The read operations.</summary>
	public ulong ReadOperationCount;

	/// <summary>The write operations.</summary>
	public ulong WriteOperationCount;

	/// <summary>The other operations.</summary>
	public ulong OtherOperationCount;

	/// <summary>The bytes read.</summary>
	public ulong ReadTransferCount;

	/// <summary>The bytes written.</summary>
	public ulong WriteTransferCount;

	/// <summary>The other bytes transferred.</summary>
	public ulong OtherTransferCount;
}
