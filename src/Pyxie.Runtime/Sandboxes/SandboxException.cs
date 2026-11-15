namespace Pyxie.Runtime.Sandboxes;

/// <summary>A sandbox could not be made or used for a reason that belongs to the project or the round, for example a broken image or a sign-in that is missing.</summary>
public sealed class SandboxException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="SandboxException"/> class.</summary>
	public SandboxException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="SandboxException"/> class.</summary>
	/// <param name="message">What failed, in words for a person.</param>
	public SandboxException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="SandboxException"/> class.</summary>
	/// <param name="message">What failed.</param>
	/// <param name="innerException">The underlying failure.</param>
	public SandboxException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
