namespace Pyxie.Runtime.Sandboxes;

/// <summary>The credential sources offered in Settings, checked independently.</summary>
public sealed record SandboxSignInStatus(
	SandboxCredentialStatus ClaudeAccount,
	SandboxCredentialStatus ClaudeApiKey,
	SandboxCredentialStatus CodexAccount,
	SandboxCredentialStatus CodexApiKey,
	SandboxCredentialStatus DeepSeekAccount,
	SandboxCredentialStatus DeepSeekApiKey);
