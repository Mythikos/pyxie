using Pyxie.Data.Enums;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Detects machine-local credentials and gives each round only its selected sign-in.</summary>
public sealed class SandboxSignIns : ISandboxSignIns
{
	private const string CodexAccountHome = "/pyxie/auth/codex-account";
	private readonly WorkspaceLayout _layout;

	/// <summary>Initializes the sign-ins for this workspace.</summary>
	public SandboxSignIns(WorkspaceLayout layout)
	{
		_layout = layout;
	}

	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, AgentAuthentication authentication)
	{
		return (harness, authentication) switch
		{
			(HarnessKind.ClaudeCode, AgentAuthentication.Account) => FromKey(_layout.ClaudeTokenPath, "CLAUDE_CODE_OAUTH_TOKEN", "Run claude setup-token and save the token"),
			(HarnessKind.ClaudeCode, AgentAuthentication.ApiKey) => FromKey(_layout.ClaudeApiKeyPath, "ANTHROPIC_API_KEY", "Save your Anthropic API key"),
			(HarnessKind.Codex, AgentAuthentication.Account) => CodexAccount(CodexLogin.PathOnHost),
			(HarnessKind.Codex, AgentAuthentication.ApiKey) => FromKey(_layout.CodexApiKeyPath, "CODEX_API_KEY", "Save your OpenAI API key"),
			(HarnessKind.DeepSeek, AgentAuthentication.Account) => DeepSeekAccountSignIn.ForAgent(DeepSeekAccountSignIn.StorePath),
			(HarnessKind.DeepSeek, AgentAuthentication.ApiKey) => FromKey(_layout.DeepSeekApiKeyPath, "DEEPSEEK_API_KEY", "Save your DeepSeek API key"),
			(HarnessKind.Other, AgentAuthentication.None) => SandboxSignIn.None,
			_ => SandboxSignIn.Missing("Choose a supported harness and authentication method in Settings → Agents."),
		};
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		string codex = CodexLogin.PathOnHost;
		string deepSeek = DeepSeekAccountSignIn.StorePath;
		return new SandboxSignInStatus(
			KeyStatus(_layout.ClaudeTokenPath),
			KeyStatus(_layout.ClaudeApiKeyPath),
			new(codex, CodexLogin.ReadAccount(codex) is not null),
			KeyStatus(_layout.CodexApiKeyPath),
			new(deepSeek, DeepSeekAccountSignIn.ReadToken(deepSeek) is not null),
			KeyStatus(_layout.DeepSeekApiKeyPath));
	}

	internal static SandboxSignIn CodexAccount(string path)
	{
		string? account = CodexLogin.ReadAccount(path);
		return account is not null
			? SandboxCredential.Create(account, CodexAccountHome, "auth.json", new Dictionary<string, string> { ["CODEX_HOME"] = CodexAccountHome, ["PYXIE_AUTH"] = "codex-account" })
			: SandboxSignIn.Missing($"Run codex login with ChatGPT using file storage. Pyxie needs the account sign-in in {path}.");
	}

	private static SandboxCredentialStatus KeyStatus(string path) => new(path, SandboxCredential.ReadKey(path) is not null);

	private static SandboxSignIn FromKey(string path, string variable, string instruction)
	{
		string? key = SandboxCredential.ReadKey(path);
		return key is null ? SandboxSignIn.Missing($"{instruction} in {path}.") : Key(key, variable);
	}

	private static SandboxSignIn Key(string key, string variable)
	{
		(string homeVariable, string home) = variable switch
		{
			"CLAUDE_CODE_OAUTH_TOKEN" => ("CLAUDE_CONFIG_DIR", "/pyxie/auth/claude-account"),
			"ANTHROPIC_API_KEY" => ("CLAUDE_CONFIG_DIR", "/pyxie/auth/claude-api"),
			"CODEX_API_KEY" => ("CODEX_HOME", "/pyxie/auth/codex-api"),
			"DEEPSEEK_API_KEY" => ("DSH_HOME", "/pyxie/auth/dsh-api"),
			_ => throw new ArgumentOutOfRangeException(nameof(variable)),
		};
		Dictionary<string, string> environment = new()
		{
			[homeVariable] = home,
			["PYXIE_AUTH"] = variable,
			["PYXIE_KEY_FILE"] = $"{home}/{variable}",
		};
		if (homeVariable == "CLAUDE_CONFIG_DIR")
		{
			environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
		}

		return SandboxCredential.Create(key, home, variable, environment);
	}
}
