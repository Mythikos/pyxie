using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Sign-ins that give nothing and run an action when an agent's sandbox asks for its sign-in. <see cref="Pyxie.Runtime.Workspaces.CardSandboxes"/> asks after it has read the target branch's tip and before it makes the bundle it hands the card in with, so the action is a person pushing to the target branch in that moment.</summary>
public sealed class MovingSignIns : ISandboxSignIns
{
	/// <summary>Gets or sets the action to run, once, when an agent's sandbox asks for a sign-in.</summary>
	public Func<Task>? Action { get; set; }

	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, string signInFolder)
	{
		// The interface is synchronous, so the action runs on the thread pool and is waited for there.
		Func<Task>? action = Action;
		Action = null;
		if (action is not null)
		{
			Task.Run(action).GetAwaiter().GetResult();
		}

		return SandboxSignIn.None;
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		return new SandboxSignInStatus("claude-oauth-token", ClaudeTokenFound: false, "auth.json", CodexSignInFound: false);
	}
}
