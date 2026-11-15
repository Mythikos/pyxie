using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>A stand-in for the sign-ins agents' sandboxes get: tests run fake agents, which need none, so it gives nothing and reports nothing found.</summary>
public sealed class StandInSignIns : ISandboxSignIns
{
	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, string signInFolder)
	{
		return SandboxSignIn.None;
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		return new SandboxSignInStatus("claude-oauth-token", ClaudeTokenFound: false, "auth.json", CodexSignInFound: false);
	}
}
