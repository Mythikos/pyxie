using Pyxie.Data.Enums;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Setup instructions and availability of a machine-local harness credential, without its contents.</summary>
/// <param name="Harness">The harness that can use it.</param>
/// <param name="Authentication">The authentication method selected on the agent.</param>
/// <param name="Name">The setting's display name.</param>
/// <param name="Found">Whether the source is available.</param>
/// <param name="Instructions">How the operator can configure the source.</param>
public sealed record HarnessCredentialGuide(HarnessKind Harness, AgentAuthentication Authentication, string Name, bool Found, string Instructions)
{
	/// <summary>Describes the sign-in sources in display order.</summary>
	/// <param name="status">Status of the supported sources.</param>
	/// <returns>Setup instructions for each source.</returns>
	public static IReadOnlyList<HarnessCredentialGuide> From(SandboxSignInStatus status) =>
	[
		new(HarnessKind.ClaudeCode, AgentAuthentication.Account, "Claude Code account", status.ClaudeAccount.Found, $"Run claude setup-token and save the token in {status.ClaudeAccount.Path}."),
		new(HarnessKind.ClaudeCode, AgentAuthentication.ApiKey, "Claude Code API key", status.ClaudeApiKey.Found, $"Save your Anthropic API key in {status.ClaudeApiKey.Path}."),
		new(HarnessKind.Codex, AgentAuthentication.Account, "Codex account", status.CodexAccount.Found, $"Run codex login with ChatGPT using file storage. Pyxie looks in {status.CodexAccount.Path}."),
		new(HarnessKind.Codex, AgentAuthentication.ApiKey, "Codex API key", status.CodexApiKey.Found, $"Save your OpenAI API key in {status.CodexApiKey.Path}."),
		new(HarnessKind.DeepSeek, AgentAuthentication.Account, "DeepSeek account", status.DeepSeekAccount.Found, $"Sign in with dsh web. Pyxie looks in {status.DeepSeekAccount.Path}."),
		new(HarnessKind.DeepSeek, AgentAuthentication.ApiKey, "DeepSeek API key", status.DeepSeekApiKey.Found, $"Save your DeepSeek API key in {status.DeepSeekApiKey.Path}."),
	];
}
