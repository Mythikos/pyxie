using System.Diagnostics;

using Xunit;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>Tells whether a process the runner started is still alive, without knowing its identifier. On Windows a process holds its working directory open, so the folder cannot be deleted while the process, or any child that inherited it, lives; once the whole tree is gone the folder can be removed. Elsewhere a folder can be deleted under a running process, so the checks do nothing and a test relies on its other assertions.</summary>
public static class ChildProcessProbe
{
	private static readonly TimeSpan s_limit = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(50);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Waits until a file exists, which the fake harness's <c>--record</c> creates when it starts.</summary>
	/// <param name="path">The file.</param>
	/// <param name="description">What is awaited, finishing the sentence "Timed out waiting for ..." in the failure message.</param>
	/// <returns>A task that completes when the file exists. The test fails when it does not within thirty seconds.</returns>
	public static async Task WaitForFileAsync(string path, string description)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (!File.Exists(path))
		{
			Assert.True(stopwatch.Elapsed < s_limit, $"Timed out waiting for {description}: {path} never appeared.");
			await Task.Delay(s_pollInterval, Token);
		}
	}

	/// <summary>Determines whether a process is using a folder as its working directory, by trying to delete the folder and its contents.</summary>
	/// <param name="folder">The folder the process was started in. Its contents are deleted when it is free.</param>
	/// <returns><see langword="true"/> when something holds the folder and it could not be removed; always <see langword="false"/> off Windows.</returns>
	public static bool IsHeld(string folder)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		try
		{
			Directory.Delete(folder, recursive: true);

			return false;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return true;
		}
	}

	/// <summary>Waits until no process holds a folder as its working directory: the processes started in it are gone.</summary>
	/// <param name="folder">The folder the processes were started in. It is deleted once free.</param>
	/// <param name="whoHeldIt">Who should be gone, for the failure message.</param>
	/// <returns>A task that completes when the folder has been deleted. The test fails when a process still holds it after thirty seconds.</returns>
	public static async Task AssertReleasedAsync(string folder, string whoHeldIt)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}

		Stopwatch stopwatch = Stopwatch.StartNew();
		while (IsHeld(folder))
		{
			Assert.True(stopwatch.Elapsed < s_limit, $"{whoHeldIt} was not killed: it still uses {folder} as its working directory {s_limit.TotalSeconds:0} seconds later.");
			await Task.Delay(s_pollInterval, Token);
		}
	}
}
