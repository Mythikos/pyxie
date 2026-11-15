using System.Diagnostics;
using System.Runtime.InteropServices;

using Pyxie.Runtime.Processes.Native;

namespace Pyxie.Runtime.Processes;

/// <summary>A Windows job that kills every process in it when its last handle closes, which happens when Pyxie exits however it exits. Windows does not end a process's children when the process dies, so without the job an agent could keep running, and spending, after Pyxie was killed, beside the card's resumed round. Inside a sandbox, Pyxie ends what an agent left running itself. On other platforms the job does nothing.</summary>
internal sealed class ChildProcessJob : IDisposable
{
	private const int ExtendedLimitInformation = 9;
	private const uint KillOnJobClose = 0x2000;

	private readonly SafeJobHandle? _job;

	/// <summary>Initializes a new instance of the <see cref="ChildProcessJob"/> class, creating the job on Windows.</summary>
	public ChildProcessJob()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		SafeJobHandle job = JobObjectMethods.CreateJobObject(IntPtr.Zero, null);
		JobExtendedLimits limits = new() { Basic = new JobBasicLimits { LimitFlags = KillOnJobClose } };
		if (job.IsInvalid || !JobObjectMethods.SetInformationJobObject(job, ExtendedLimitInformation, ref limits, Marshal.SizeOf<JobExtendedLimits>()))
		{
			job.Dispose();
			return;
		}

		_job = job;
	}

	/// <summary>Puts a started process in the job. Its children join it as they start. Best effort: a process that cannot join still runs.</summary>
	/// <param name="process">The process, just started.</param>
	public void Add(Process process)
	{
		if (_job is not null && OperatingSystem.IsWindows())
		{
			JobObjectMethods.AssignProcessToJobObject(_job, process.SafeHandle);
		}
	}

	/// <summary>Closes the job, which ends every process still in it.</summary>
	public void Dispose()
	{
		_job?.Dispose();
	}
}
