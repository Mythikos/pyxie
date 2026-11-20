using Pyxie.Data.Enums;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Decides what an agent's sandbox is given to sign in, by its harness.</summary>
public interface ISandboxSignIns
{
	/// <summary>Gets what an agent of a harness gets in its sandbox.</summary>
	/// <param name="harness">The agent's harness.</param>
	/// <param name="authentication">The only credential method to supply.</param>
	/// <returns>The sign-in, or why there is none.</returns>
	SandboxSignIn ForAgent(HarnessKind harness, AgentAuthentication authentication);

	/// <summary>Gets whether the sign-ins are there.</summary>
	/// <returns>The status.</returns>
	SandboxSignInStatus GetStatus();
}
