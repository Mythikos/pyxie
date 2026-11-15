namespace Pyxie.Runtime.Sandboxes;

/// <summary>The container engine does not answer, or does not run Linux containers. Nothing about the card is wrong, so the round is given back and the pipeline stops, rather than the card losing an attempt.</summary>
public sealed class SandboxUnavailableException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="SandboxUnavailableException"/> class.</summary>
	public SandboxUnavailableException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="SandboxUnavailableException"/> class.</summary>
	/// <param name="message">What is wrong, in words for a person.</param>
	public SandboxUnavailableException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="SandboxUnavailableException"/> class.</summary>
	/// <param name="message">What is wrong.</param>
	/// <param name="innerException">The underlying failure.</param>
	public SandboxUnavailableException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
