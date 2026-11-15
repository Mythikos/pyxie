namespace Pyxie.Runtime.Git;

/// <summary>The hub's git hooks. The <c>update</c> hook runs for every branch a push to the hub changes. People own their branches in the hub: they push, force-push, and delete them as they like. Two kinds of branch are kept safe: the target branch only moves forward and is never deleted, because cards are built on it, and branches under <c>pyxie/</c> belong to the pipeline. Agents never reach the hub: their work comes back from their sandboxes as bundles Pyxie fetches. Pyxie's own merges and branch moves are not pushes, so the hook never stands in their way.</summary>
internal static class HubHooks
{
	/// <summary>The hub's git setting that names the target branch, which the update hook reads.</summary>
	public const string TargetBranchKey = "pyxie.targetBranch";

	// git passes a zero object name, of any length, for a branch that does not exist, so a zero new name means the push deletes the branch. When merge-base cannot tell, the push is refused.
	private const string UpdateScript = """
		#!/bin/sh
		# Written by Pyxie when it set up this hub. People own their branches here; the target branch only moves
		# forward and is never deleted, because cards are built on it, and branches under pyxie/ belong to the pipeline.
		refname=$1
		old=$2
		new=$3
		target=$(git config --get pyxie.targetBranch)

		case $refname in
		refs/heads/pyxie/*)
			echo "${refname#refs/heads/} belongs to Pyxie's pipeline: branches under pyxie/ cannot be pushed to." >&2
			exit 1
			;;
		esac

		if [ -n "$target" ] && [ "$refname" = "refs/heads/$target" ]; then
			case $new in
			*[!0]*) ;;
			*)
				echo "$target cannot be deleted: cards are built on it." >&2
				exit 1
				;;
			esac

			case $old in
			*[!0]*)
				if ! git merge-base --is-ancestor "$old" "$new"; then
					echo "$target cannot be rewritten: cards are built on it. Pull, then push again." >&2
					exit 1
				fi
				;;
			esac
		fi

		exit 0
		""";

	/// <summary>Writes the hook into a hooks folder, replacing any hook of the same name, and makes it executable where files need to be.</summary>
	/// <param name="hooksFolder">The hub's hooks folder.</param>
	/// <param name="cancellationToken">A token to cancel the write.</param>
	/// <returns>A task that completes when the hook is written.</returns>
	public static async Task WriteAsync(string hooksFolder, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(hooksFolder);
		await WriteHookAsync(Path.Combine(hooksFolder, "update"), UpdateScript, cancellationToken);
	}

	private static async Task WriteHookAsync(string path, string script, CancellationToken cancellationToken)
	{
		// git runs a hook with sh, which needs Unix line endings, and a byte order mark would hide the #! line; File.WriteAllTextAsync writes UTF-8 without one.
		await File.WriteAllTextAsync(path, script.ReplaceLineEndings("\n"), cancellationToken);

		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
		}
	}
}
