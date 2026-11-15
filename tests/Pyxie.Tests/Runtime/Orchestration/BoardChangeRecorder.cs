using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Enums;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Looks at the database every time the pipeline announces that cards changed, and remembers what it saw: the lane of every card and how many rounds were running. The pipeline announces a change right after it makes one and before it makes the next (a round takes a process start at least), so what the recorder sees is each state the board really went through, which polling the board could miss. Handlers run on the announcing thread and must not throw, so a failure to read is kept and thrown by <see cref="ThrowIfFailed"/>.</summary>
public sealed class BoardChangeRecorder : IDisposable
{
	private readonly RuntimeHarness _harness;
	private readonly Lock _gate = new();
	private readonly List<IReadOnlyList<(int CardId, CardLane Lane)>> _snapshots = [];
	private int _maxOpenRounds;
	private Exception? _failure;

	/// <summary>Initializes a new instance of the <see cref="BoardChangeRecorder"/> class and starts listening.</summary>
	/// <param name="harness">The runtime whose card changes to record.</param>
	public BoardChangeRecorder(RuntimeHarness harness)
	{
		_harness = harness;
		_harness.Activity.CardsChanged += OnCardsChanged;
	}

	/// <summary>Gets the most rounds that were running at once at any announcement.</summary>
	public int MaxOpenRounds
	{
		get
		{
			lock (_gate)
			{
				return _maxOpenRounds;
			}
		}
	}

	/// <summary>Gets the lanes a card was seen in, in order, with a lane that was seen several times in a row listed once.</summary>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The lanes; empty when the card was never seen.</returns>
	public IReadOnlyList<CardLane> LanesOf(int cardId)
	{
		lock (_gate)
		{
			List<CardLane> lanes = [];

			foreach (IReadOnlyList<(int CardId, CardLane Lane)> snapshot in _snapshots)
			{
				foreach ((int id, CardLane lane) in snapshot)
				{
					if (id == cardId && (lanes.Count == 0 || lanes[^1] != lane))
					{
						lanes.Add(lane);
					}
				}
			}

			return lanes;
		}
	}

	/// <summary>Fails the test with the exception that stopped the recorder from reading the database, if there was one.</summary>
	public void ThrowIfFailed()
	{
		lock (_gate)
		{
			if (_failure is not null)
			{
				throw new InvalidOperationException("The board recorder could not read the database.", _failure);
			}
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		_harness.Activity.CardsChanged -= OnCardsChanged;
	}

	private void OnCardsChanged()
	{
		try
		{
			using ProjectDbContext context = _harness.ContextFactory.CreateDbContext();
			List<(int CardId, CardLane Lane)> lanes = [.. context.Cards.AsNoTracking().OrderBy(card => card.Id).Select(card => new { card.Id, card.Lane }).AsEnumerable().Select(card => (card.Id, card.Lane))];
			int open = context.Rounds.Count(round => round.Outcome == null);

			lock (_gate)
			{
				_snapshots.Add(lanes);
				_maxOpenRounds = Math.Max(_maxOpenRounds, open);
			}
		}
		catch (Exception exception)
		{
			lock (_gate)
			{
				_failure ??= exception;
			}
		}
	}
}
