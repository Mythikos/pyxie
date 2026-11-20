namespace Pyxie.Data.Enums;

/// <summary>The credential a round is allowed to use.</summary>
public enum AgentAuthentication
{
	/// <summary>No managed credential for a custom harness.</summary>
	None,

	/// <summary>The harness account sign-in.</summary>
	Account,

	/// <summary>The provider API key.</summary>
	ApiKey,
}
