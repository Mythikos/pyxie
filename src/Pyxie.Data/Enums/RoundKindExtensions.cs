namespace Pyxie.Data.Enums;

/// <summary>Presentation and classification helpers for <see cref="RoundKind"/>.</summary>
public static class RoundKindExtensions
{
	/// <summary>Gets the name people see for a round kind in card history.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns>The display name.</returns>
	public static string DisplayName(this RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work => "Work",
			RoundKind.Gate => "Gates",
			RoundKind.Review => "Review",
			RoundKind.Merge => "Merge",
			RoundKind.Resolve => "Resolve",
			_ => kind.ToString(),
		};
	}

	/// <summary>Determines whether rounds of a kind run an agent, and so carry a briefing and a copy of the agent.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns><see langword="true"/> for work, review, and resolve rounds.</returns>
	public static bool RunsAgent(this RoundKind kind)
	{
		return kind is RoundKind.Work or RoundKind.Review or RoundKind.Resolve;
	}

	/// <summary>Determines whether rounds of a kind change the card's work with an agent, so an earlier approval no longer covers what the card would merge.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns><see langword="true"/> for work and resolve rounds.</returns>
	public static bool ChangesWork(this RoundKind kind)
	{
		return kind is RoundKind.Work or RoundKind.Resolve;
	}
}
