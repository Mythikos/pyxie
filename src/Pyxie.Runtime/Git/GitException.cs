namespace Pyxie.Runtime.Git;

/// <summary>A git command failed in a way its caller did not expect, for example a missing repository or a corrupt object. Expected outcomes, such as a merge conflict, are results.</summary>
public sealed class GitException : Exception
{
	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	public GitException()
	{
	}

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">What failed, with git's own output.</param>
	public GitException(string message)
		: base(message)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="GitException"/> class.</summary>
	/// <param name="message">What failed.</param>
	/// <param name="innerException">The underlying failure.</param>
	public GitException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
