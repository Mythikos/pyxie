using System.Diagnostics;

using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

/// <summary>A <c>reference-transaction</c> hook in a hub that steps into the first ref update of one branch, for moments a test cannot reach by timing: <see cref="RaceAsync"/> refuses that update and then moves the branch somewhere else, as if somebody had moved it just before Pyxie's compare-and-swap, and <see cref="HoldAsync"/> holds the update, with the branch locked, until the test releases it. It acts once and lets every other ref update through. Disposing it releases a held update, removes the hook, and deletes its marker files.</summary>
/// <remarks>git reports an update the hook refused the way it reports a lost compare-and-swap, a failed <c>update-ref</c>, and <c>GitClient.TryMoveBranchAsync</c> tells the two apart by reading the branch back; finding it moved, it reports the race, which is what the integrator then handles. git runs the hook with its own <c>sh</c>, which accepts Windows paths with forward slashes.</remarks>
public sealed class BranchUpdateHook : IAsyncDisposable
{
	// Each poll sleeps 0.05 seconds, so a held update gives up after about as long as RuntimeTestTimeouts.Wait, and a test that never releases it cannot leave git running.
	private const int HoldPollLimit = 3000;
	private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(50);

	private readonly string _hookPath;
	private readonly string _folder;
	private readonly string _armedPath;
	private readonly string _heldPath;
	private readonly string _releasePath;
	private readonly string _finishedPath;

	private BranchUpdateHook(string hookPath, string folder)
	{
		_hookPath = hookPath;
		_folder = folder;
		_armedPath = Path.Combine(folder, "armed");
		_heldPath = Path.Combine(folder, "held");
		_releasePath = Path.Combine(folder, "release");
		_finishedPath = Path.Combine(folder, "finished");
	}

	/// <summary>Gets a value indicating whether the hook has acted: it refused and moved, or held, the branch's update.</summary>
	public bool IsSpent => !File.Exists(_armedPath);

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	/// <summary>Installs a hook that refuses the next update of a branch and then moves the branch to another commit.</summary>
	/// <param name="hub">The hub.</param>
	/// <param name="branch">The branch, without <c>refs/heads/</c>.</param>
	/// <param name="commit">Where the branch moves; the hub must have it.</param>
	/// <returns>The installed, armed hook.</returns>
	public static async Task<BranchUpdateHook> RaceAsync(TempGitRepository hub, string branch, string commit)
	{
		return await InstallAsync(hub, hook => $$"""
			#!/bin/sh
			# Written by a test: refuses the first update of refs/heads/{{branch}}, then moves the branch to {{commit}}, as somebody else would have.
			armed="{{Slashes(hook._armedPath)}}"
			[ -f "$armed" ] || exit 0
			hit=
			while read -r old new ref; do
				if [ "$ref" = "refs/heads/{{branch}}" ]; then hit=1; fi
			done
			[ -n "$hit" ] || exit 0
			case "$1" in
			prepared)
				exit 1
				;;
			aborted)
				rm -f "$armed"
				git update-ref "refs/heads/{{branch}}" "{{commit}}"
				;;
			esac
			exit 0
			""");
	}

	/// <summary>Installs a hook that holds the next update of a branch, after git has locked the branch, until <see cref="Release"/>.</summary>
	/// <param name="hub">The hub.</param>
	/// <param name="branch">The branch, without <c>refs/heads/</c>.</param>
	/// <returns>The installed, armed hook.</returns>
	public static async Task<BranchUpdateHook> HoldAsync(TempGitRepository hub, string branch)
	{
		return await InstallAsync(hub, hook => $$"""
			#!/bin/sh
			# Written by a test: holds the first update of refs/heads/{{branch}} until the test releases it.
			armed="{{Slashes(hook._armedPath)}}"
			[ -f "$armed" ] || exit 0
			hit=
			while read -r old new ref; do
				if [ "$ref" = "refs/heads/{{branch}}" ]; then hit=1; fi
			done
			[ -n "$hit" ] && [ "$1" = "prepared" ] || exit 0
			rm -f "$armed"
			echo held > "{{Slashes(hook._heldPath)}}"
			polls=0
			while [ ! -f "{{Slashes(hook._releasePath)}}" ] && [ "$polls" -lt {{HoldPollLimit}} ]; do
				sleep 0.05
				polls=$((polls + 1))
			done
			echo finished > "{{Slashes(hook._finishedPath)}}"
			exit 0
			""");
	}

	/// <summary>Waits until git is held in the hook.</summary>
	/// <returns>A task that completes when the update is held. The test fails when git does not reach the hook within <see cref="RuntimeTestTimeouts.Wait"/>.</returns>
	public async Task WaitUntilHeldAsync()
	{
		bool held = await WaitForFileAsync(_heldPath, Token);

		Assert.True(held, $"git never reached the hook that holds the branch's update: {_heldPath} did not appear within {RuntimeTestTimeouts.Wait}.");
	}

	/// <summary>Lets a held update go on.</summary>
	public void Release()
	{
		File.WriteAllText(_releasePath, string.Empty);
	}

	/// <summary>Releases a held update and waits for git to leave the hook, then removes the hook and its marker files.</summary>
	/// <returns>A task that completes when the hook is gone.</returns>
	public async ValueTask DisposeAsync()
	{
		Release();

		// A failing test may never have let git go on, and git still runs the hook from the hub's folder; the hook leaves at once now, so this wait is short.
		if (File.Exists(_heldPath))
		{
			await WaitForFileAsync(_finishedPath, CancellationToken.None);
		}

		File.Delete(_hookPath);
		await TempFolder.DeleteAsync(_folder);
	}

	private static async Task<BranchUpdateHook> InstallAsync(TempGitRepository hub, Func<BranchUpdateHook, string> script)
	{
		// The hub names its own hooks folder (core.hooksPath), and --git-path follows it.
		string hooks = await hub.RunGitAsync("rev-parse", "--path-format=absolute", "--git-path", "hooks");
		BranchUpdateHook hook = new(Path.Combine(Path.GetFullPath(hooks), "reference-transaction"), TempFolder.Create("pyxie-hook"));

		await File.WriteAllTextAsync(hook._hookPath, script(hook).ReplaceLineEndings("\n"), Token);
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(hook._hookPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		await File.WriteAllTextAsync(hook._armedPath, string.Empty, Token);

		return hook;
	}

	private static async Task<bool> WaitForFileAsync(string path, CancellationToken cancellationToken)
	{
		Stopwatch watch = Stopwatch.StartNew();
		while (!File.Exists(path))
		{
			if (watch.Elapsed > RuntimeTestTimeouts.Wait)
			{
				return false;
			}

			await Task.Delay(s_pollInterval, cancellationToken);
		}

		return true;
	}

	private static string Slashes(string path)
	{
		return path.Replace('\\', '/');
	}
}
