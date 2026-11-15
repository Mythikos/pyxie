using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services.ReadModels;

namespace Pyxie.Web.Components.Board;

/// <summary>A card's rounds: a summary line with counts and the rounds oldest first, each expandable to its report and briefing.</summary>
public partial class CardHistory
{
	private readonly HashSet<int> _expanded = [];

	/// <summary>Gets or sets what the rounds add up to.</summary>
	[Parameter]
	public CardHistorySummary Summary { get; set; } = CardHistorySummary.Empty;

	/// <summary>Gets or sets the rounds to show, in any order.</summary>
	[Parameter]
	public IReadOnlyList<CardRound> Rounds { get; set; } = [];

	private IEnumerable<CardRound> Ordered => Rounds.OrderBy(round => round.Sequence);

	private string SummaryText => HistorySummaryText.Of(Summary);

	private static bool HasDetails(CardRound round)
	{
		return !string.IsNullOrWhiteSpace(round.Report) || !string.IsNullOrWhiteSpace(round.Briefing);
	}

	private void Toggle(int roundId)
	{
		if (!_expanded.Remove(roundId))
		{
			_expanded.Add(roundId);
		}
	}
}
