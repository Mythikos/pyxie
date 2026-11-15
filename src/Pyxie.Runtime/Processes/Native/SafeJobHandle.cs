using Microsoft.Win32.SafeHandles;

namespace Pyxie.Runtime.Processes.Native;

/// <summary>A job object handle. Closing it ends the processes in a job created with kill-on-close.</summary>
internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
{
	/// <summary>Initializes a new instance of the <see cref="SafeJobHandle"/> class; the interop marshaller sets the handle.</summary>
	public SafeJobHandle()
		: base(ownsHandle: true)
	{
	}

	/// <inheritdoc/>
	protected override bool ReleaseHandle()
	{
		return OperatingSystem.IsWindows() && JobObjectMethods.CloseHandle(handle);
	}
}
