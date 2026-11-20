using System.Text.Json;

using Pyxie.Runtime.Processes;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Supplies only the official DeepSeek account grant to DSH's headless profile. A DSH store can also contain unrelated providers' credentials.</summary>
internal static class DeepSeekAccountSignIn
{
	private const string Issuer = "https://platform.deepseek.com";
	private const string Record = "deepseek-account-platform/default";
	private const string ContainerHome = "/pyxie/auth/dsh-account";

	/// <summary>Gets the credential file written by the person's DSH sign-in.</summary>
	internal static string StorePath
	{
		get
		{
			string? home = Environment.GetEnvironmentVariable("DSH_HOME");
			string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string root = home switch
			{
				null or "" => Path.Combine(profile, ".dsh"),
				"~" => profile,
				_ when string.IsNullOrWhiteSpace(home) => Path.Combine(profile, ".dsh"),
				_ when home.StartsWith("~/", StringComparison.Ordinal) || home.StartsWith(@"~\", StringComparison.Ordinal) => Path.Combine(profile, home[2..]),
				_ => home,
			};
			return Path.GetFullPath(Path.Combine(root, ".credentials.yaml"));
		}
	}

	/// <summary>Gets the sign-in for an account-authenticated DSH round.</summary>
	/// <param name="path">The DSH credential file.</param>
	internal static SandboxSignIn ForAgent(string path)
	{
		string? token = ReadToken(path);
		return token is null
			? SandboxSignIn.Missing($"Sign in with dsh web. Pyxie needs a DeepSeek account grant in {path}.")
			: new SandboxSignIn([], [], new Dictionary<string, string> { ["DSH_HOME"] = ContainerHome, ["PYXIE_AUTH"] = "deepseek-account" }, null)
			{
				PrepareAsync = (sandbox, cancellationToken) => InstallAsync(sandbox, token, cancellationToken),
			};
	}

	/// <summary>Reads a grant for the official platform. Errors from the YAML parser may contain credential text and must never reach logs or the UI.</summary>
	internal static string? ReadToken(string path)
	{
		try
		{
			using StreamReader reader = File.OpenText(path);
			YamlStream store = new();
			store.Load(reader);
			if (store.Documents.Count != 1)
			{
				return null;
			}

			YamlNode root = store.Documents[0].RootNode;
			YamlNode? grant = Child(Child(root, "records"), Record);
			YamlNode? payload = Child(grant, "payload");
			string? token = Scalar(payload, "token");
			return Scalar(root, "version") == "1" && Scalar(grant, "kind") == "grant" && Scalar(payload, "version") == "1" && Scalar(payload, "issuer") == Issuer && !string.IsNullOrWhiteSpace(token) ? token : null;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlException)
		{
			return null;
		}
	}

	private static YamlNode? Child(YamlNode? node, string key) => node is YamlMappingNode map && map.Children.TryGetValue(key, out YamlNode? value) ? value : null;

	private static string? Scalar(YamlNode? node, string key) => (Child(node, key) as YamlScalarNode)?.Value;

	private static async Task InstallAsync(ISandbox sandbox, string token, CancellationToken cancellationToken)
	{
		// Start with a fresh private home even on a reused worker. The only secret travels on stdin.
		// The owner-only Linux file also lets DSH invalidate an expired grant without touching the host.
		const string install = """
			set -eu
			umask 077
			rm -rf -- "$1"
			mkdir -p "$1/profiles/headless"
			cat > "$1/.credentials.yaml"
			cat > "$1/profiles/headless/cordis.patch.yml" <<'PROFILE'
			- id: credentials
			  disabled: false
			- id: authorization
			  disabled: false
			- id: deepseek-account
			  disabled: false
			- id: agent-default-model
			  config:
			    provider: deepseek-account
			    model: deepseek-flash
			PROFILE
			""";
		string document = $$"""
			version: 1
			records:
			  {{Record}}:
			    kind: grant
			    payload:
			      version: 1
			      issuer: {{Issuer}}
			      token: {{JsonSerializer.Serialize(token)}}
			""";
		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", install, "sh", ContainerHome], sandbox.WorkPath) { StandardInput = document }, cancellationToken);
		if (!result.Succeeded)
		{
			throw new SandboxException("The sandbox could not prepare the DeepSeek account sign-in.");
		}
	}
}
