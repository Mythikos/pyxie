namespace Pyxie.Runtime.Sandboxes;

/// <summary>What a sandbox is for, which decides how long it lives and what it is given.</summary>
public enum SandboxRole
{
	/// <summary>A card's worker: kept between the card's work rounds, with its own volumes for the work and the agent's home.</summary>
	Worker = 0,

	/// <summary>A gate round: a fresh sandbox each time, with no sign-in.</summary>
	Gates = 1,

	/// <summary>A review round: a fresh sandbox each time; nothing comes back from it but the result.</summary>
	Review = 2,

	/// <summary>A resolve round: a fresh sandbox each time, with no volumes, where the merge agent resolves a merge Pyxie started; nothing comes back from it but one merge commit and the result.</summary>
	Resolve = 3,
}
