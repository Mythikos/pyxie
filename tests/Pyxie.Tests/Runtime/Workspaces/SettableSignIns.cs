using System.Collections.Concurrent;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Runtime.Workspaces;

/// <summary>Sign-ins that give every agent the same answer the test chose, and remember who asked, so a test sees what an agent's sandbox gets and when it is asked at all.</summary>
public sealed class SettableSignIns : ISandboxSignIns
{
	private readonly SandboxSignIn _answer;
	private readonly ConcurrentQueue<(HarnessKind Harness, AgentAuthentication Authentication)> _requests = new();

	/// <summary>Initializes a new instance of the <see cref="SettableSignIns"/> class.</summary>
	/// <param name="answer">What every agent gets.</param>
	public SettableSignIns(SandboxSignIn answer)
	{
		_answer = answer;
	}

	/// <summary>Gets each request, in order: the agent's harness and the sign-in folder it was offered.</summary>
	public IReadOnlyList<(HarnessKind Harness, AgentAuthentication Authentication)> Requests => [.. _requests];

	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, AgentAuthentication authentication)
	{
		_requests.Enqueue((harness, authentication));

		return _answer;
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		return new SandboxSignInStatus(new("claude-oauth-token", false), new("anthropic-api-key", false), new("auth.json", false), new("openai-api-key", false), new(".credentials.yaml", false), new("deepseek-api-key", false));
	}
}
