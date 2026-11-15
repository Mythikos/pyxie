using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A stand-in for the sign-ins agents' sandboxes get that reports the status a test chose, so the settings page can be shown with each sign-in found or missing.</summary>
internal sealed class FixedSignIns : ISandboxSignIns
{
	private readonly SandboxSignInStatus _status;

	/// <summary>Initializes a new instance of the <see cref="FixedSignIns"/> class.</summary>
	/// <param name="status">The status to report.</param>
	public FixedSignIns(SandboxSignInStatus status)
	{
		_status = status;
	}

	/// <inheritdoc/>
	public SandboxSignIn ForAgent(HarnessKind harness, string signInFolder)
	{
		return SandboxSignIn.None;
	}

	/// <inheritdoc/>
	public SandboxSignInStatus GetStatus()
	{
		return _status;
	}
}
