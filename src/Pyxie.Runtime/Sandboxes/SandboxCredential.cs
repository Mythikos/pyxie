using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Copies the current credential over stdin into a private file in the opened container, including on worker reuse.</summary>
internal static class SandboxCredential
{
	internal static string? Normalize(string? value)
	{
		string? token = value?.Trim();
		return !string.IsNullOrEmpty(token) && token.All(character => character is >= '!' and <= '~') ? token : null;
	}

	internal static string? ReadKey(string path)
	{
		try
		{
			return Normalize(File.ReadAllText(path));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	internal static SandboxSignIn Create(string value, string folder, string file, Dictionary<string, string> environment)
	{
		// Replace the file rather than following a link left by a prior round. All path arguments are Pyxie constants.
		const string install = """
			set -eu
			umask 077
			if [ -L "$1" ]; then rm -f -- "$1"; fi
			mkdir -p -- "$1"
			tmp=$(mktemp "$1/.pyxie-auth.XXXXXX")
			trap 'rm -f -- "$tmp"' EXIT
			cat > "$tmp"
			mv -fT -- "$tmp" "$1/$2"
			""";

		return new SandboxSignIn([], [], environment, null)
		{
			PrepareAsync = async (sandbox, cancellationToken) =>
			{
				ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", install, "sh", folder, file], sandbox.WorkPath) { StandardInput = value }, cancellationToken);
				if (!result.Succeeded)
				{
					throw new SandboxException("The sandbox could not prepare the selected sign-in.");
				}
			},
		};
	}
}
