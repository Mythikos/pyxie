namespace Pyxie.Data.Enums;

/// <summary>Presentation helpers for <see cref="CardLane"/>.</summary>
public static class CardLaneExtensions
{
	/// <summary>Gets the name people see for a lane, in lane headers and in messages.</summary>
	/// <param name="lane">The lane.</param>
	/// <returns>The display name, such as <c>In Progress</c> for <see cref="CardLane.InProgress"/>.</returns>
	public static string DisplayName(this CardLane lane)
	{
		return lane switch
		{
			CardLane.Queue => "Queue",
			CardLane.InProgress => "In Progress",
			CardLane.Gates => "Gates",
			CardLane.Review => "Review",
			CardLane.Complete => "Complete",
			CardLane.Closed => "Closed",
			_ => lane.ToString(),
		};
	}
}
