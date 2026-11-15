namespace Pyxie.Web.Components.Board;

/// <summary>Words a card's attempts the way the board and the card dialog show them.</summary>
public static class AttemptProgress
{
	/// <summary>Gets how far a card is through its attempts, in words, as the card dialog shows it.</summary>
	/// <param name="attempt">The attempt the card is on in its current run, or 0 while it has not started one.</param>
	/// <param name="attemptLimit">How many attempts each run gets.</param>
	/// <returns>For example <c>2 of 3</c>; only the limit when no attempt has started.</returns>
	public static string Text(int attempt, int attemptLimit)
	{
		return attempt > 0 ? $"{attempt} of {attemptLimit}" : $"{attemptLimit}";
	}

	/// <summary>Gets how far a card is through its attempts in the short form on a board card.</summary>
	/// <param name="attempt">The attempt the card is on in its current run, or 0 while it has not started one.</param>
	/// <param name="attemptLimit">How many attempts each run gets.</param>
	/// <returns>For example <c>2/3</c>; only the limit when no attempt has started.</returns>
	public static string Compact(int attempt, int attemptLimit)
	{
		return attempt > 0 ? $"{attempt}/{attemptLimit}" : $"{attemptLimit}";
	}
}
