namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Conditions for the <c>SkipUnless</c> property of xUnit's test attributes, so a test that needs Windows (batch files, a working directory that cannot be deleted while a process uses it) is reported as skipped elsewhere.</summary>
public static class HostPlatform
{
	/// <summary>The skip reason for a test that only makes sense on Windows.</summary>
	public const string NeedsWindows = "Needs Windows: batch files and working directories that cannot be deleted while a process uses them exist only there.";

	/// <summary>The skip reason for a test of behavior that exists only off Windows.</summary>
	public const string NeedsAnotherPlatform = "Needs a platform other than Windows.";

	/// <summary>Gets a value indicating whether the tests run on Windows.</summary>
	public static bool IsWindows => OperatingSystem.IsWindows();

	/// <summary>Gets a value indicating whether the tests run on something other than Windows.</summary>
	public static bool IsNotWindows => !OperatingSystem.IsWindows();
}
