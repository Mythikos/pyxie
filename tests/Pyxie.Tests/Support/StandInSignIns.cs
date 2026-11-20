using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>A stand-in for the sign-ins agents' sandboxes get: tests run fake agents, which need none, so it gives nothing and reports nothing found.</summary>
public sealed class StandInSignIns : ISandboxSignIns
{
	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, AgentAuthentication authentication)
	{
		return SandboxSignIn.None;
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		return new SandboxSignInStatus(new("claude-oauth-token", false), new("anthropic-api-key", false), new("auth.json", false), new("openai-api-key", false), new(".credentials.yaml", false), new("deepseek-api-key", false));
	}
}
