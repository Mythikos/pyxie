using Pyxie.Data.Enums;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Signs agents in with the person's own subscriptions (see <c>docs/sandboxing.md</c>). Claude Code gets the token <c>claude setup-token</c> printed, which the person saved in the workspace root's <c>secrets</c> folder. Codex gets a copy of the person's Codex sign-in, made fresh for each sandbox, so a sandbox that refreshes its tokens never writes to the person's own. OpenCode and other harnesses get nothing from Pyxie.</summary>
public sealed class SandboxSignIns : ISandboxSignIns
{
	private const string CodexHomeInside = "/pyxie/codex";
	private const string CodexAuthFile = "auth.json";

	private readonly WorkspaceLayout _layout;

	/// <summary>Initializes a new instance of the <see cref="SandboxSignIns"/> class.</summary>
	/// <param name="layout">The workspace layout, which places the Claude Code token.</param>
	public SandboxSignIns(WorkspaceLayout layout)
	{
		_layout = layout;
	}

	// Codex keeps its sign-in in CODEX_HOME, or in .codex in the person's profile.
	private static string CodexAuthPath
	{
		get
		{
			string? home = Environment.GetEnvironmentVariable("CODEX_HOME");
			string folder = string.IsNullOrWhiteSpace(home) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : home;

			return Path.Combine(folder, CodexAuthFile);
		}
	}

	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, string signInFolder)
	{
		return harness switch
		{
			HarnessKind.ClaudeCode => ClaudeCode(),
			HarnessKind.Codex => Codex(signInFolder),
			_ => SandboxSignIn.None,
		};
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		string token = _layout.ClaudeTokenPath;
		string codex = CodexAuthPath;

		return new SandboxSignInStatus(token, HasText(token), codex, File.Exists(codex));
	}

	private static bool HasText(string path)
	{
		return File.Exists(path) && new FileInfo(path).Length > 0;
	}

	private static SandboxSignIn Codex(string signInFolder)
	{
		string source = CodexAuthPath;
		if (!File.Exists(source))
		{
			return SandboxSignIn.Missing($"Codex is not signed in on this machine: run codex login. Pyxie copies {source} into each sandbox.");
		}

		Directory.CreateDirectory(signInFolder);
		File.Copy(source, Path.Combine(signInFolder, CodexAuthFile), overwrite: true);

		return new SandboxSignIn(
			[],
			[new SandboxMount(signInFolder, CodexHomeInside, ReadOnly: false)],
			new Dictionary<string, string> { ["CODEX_HOME"] = CodexHomeInside },
			null);
	}

	private SandboxSignIn ClaudeCode()
	{
		string token = _layout.ClaudeTokenPath;
		if (!HasText(token))
		{
			return SandboxSignIn.Missing($"There is no Claude Code token: run claude setup-token and save the token it prints in {token}.");
		}

		return new SandboxSignIn(
			[new SandboxSecret(token, "CLAUDE_CODE_OAUTH_TOKEN")],
			[],
			new Dictionary<string, string> { ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1" },
			null);
	}
}
