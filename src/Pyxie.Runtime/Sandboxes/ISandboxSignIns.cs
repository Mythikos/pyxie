using Pyxie.Data.Enums;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Decides what an agent's sandbox is given to sign in, by its harness.</summary>
public interface ISandboxSignIns
{
	/// <summary>Gets what an agent of a harness gets in its sandbox.</summary>
	/// <param name="harness">The agent's harness.</param>
	/// <param name="signInFolder">A host folder of the sandbox's own that Pyxie may copy a sign-in into.</param>
	/// <returns>The sign-in, or why there is none.</returns>
	SandboxSignIn ForAgent(HarnessKind harness, string signInFolder);

	/// <summary>Gets whether the sign-ins are there.</summary>
	/// <returns>The status.</returns>
	SandboxSignInStatus GetStatus();
}
