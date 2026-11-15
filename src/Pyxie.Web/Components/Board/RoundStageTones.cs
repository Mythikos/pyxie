using Pyxie.Data.Enums;

namespace Pyxie.Web.Components.Board;

/// <summary>Chooses the <see cref="RoundStageTone"/> of a round kind. The stage badge and the outline of the running round in the trail both take their color from here.</summary>
public static class RoundStageTones
{
	/// <summary>Gets the tone of a round kind.</summary>
	/// <param name="kind">The round kind.</param>
	/// <returns>The tone; work and review rounds share one, since both are an agent's turn.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined round kind.</exception>
	public static RoundStageTone Of(RoundKind kind)
	{
		return kind switch
		{
			RoundKind.Work or RoundKind.Review => RoundStageTone.Work,
			RoundKind.Gate => RoundStageTone.Gate,
			RoundKind.Resolve => RoundStageTone.Resolve,
			RoundKind.Merge => RoundStageTone.Merge,
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The round kind has no stage tone."),
		};
	}
}
