using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace Pyxie.Runtime.Processes.Native;

/// <summary>The Windows job object functions <see cref="ChildProcessJob"/> uses.</summary>
[SupportedOSPlatform("windows")]
internal static partial class JobObjectMethods
{
	/// <summary>Creates an unnamed job object.</summary>
	/// <param name="attributes">Security attributes; zero for the defaults.</param>
	/// <param name="name">The job's name, or <see langword="null"/>.</param>
	/// <returns>The job handle, invalid when creation failed.</returns>
	[LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	internal static partial SafeJobHandle CreateJobObject(IntPtr attributes, string? name);

	/// <summary>Sets a job's extended limits.</summary>
	/// <param name="job">The job.</param>
	/// <param name="informationClass">The kind of information; 9 for extended limits.</param>
	/// <param name="information">The limits.</param>
	/// <param name="length">The size of <paramref name="information"/> in bytes.</param>
	/// <returns><see langword="true"/> on success.</returns>
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool SetInformationJobObject(SafeJobHandle job, int informationClass, ref JobExtendedLimits information, int length);

	/// <summary>Puts a process in a job.</summary>
	/// <param name="job">The job.</param>
	/// <param name="process">The process.</param>
	/// <returns><see langword="true"/> on success.</returns>
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

	/// <summary>Closes a kernel handle.</summary>
	/// <param name="handle">The handle.</param>
	/// <returns><see langword="true"/> on success.</returns>
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static partial bool CloseHandle(IntPtr handle);
}
