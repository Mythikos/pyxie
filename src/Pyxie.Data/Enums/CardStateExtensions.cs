namespace Pyxie.Data.Enums;

/// <summary>Presentation helpers for <see cref="CardState"/>.</summary>
public static class CardStateExtensions
{
	/// <summary>Gets the name people see for a card state.</summary>
	/// <param name="state">The state.</param>
	/// <returns>The display name.</returns>
	public static string DisplayName(this CardState state)
	{
		return state switch
		{
			CardState.Ready => "Ready",
			CardState.Error => "Error",
			_ => state.ToString(),
		};
	}
}
