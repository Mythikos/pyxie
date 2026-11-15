using Pyxie.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

/// <summary>Every announcement that cards changed raises the event once and wakes everyone who is waiting for the next change.</summary>
public sealed class PipelineActivityTests
{
	[Fact]
	public void NotifyCardsChanged_RaisesCardsChangedOncePerAnnouncement()
	{
		PipelineActivity activity = new();
		int changes = 0;
		activity.CardsChanged += () => changes++;

		activity.NotifyCardsChanged();
		activity.NotifyCardsChanged();

		Assert.Equal(2, changes);
	}

	[Fact]
	public async Task NotifyCardsChanged_ReleasesEveryWaiterAtOnceAndNotTheOnesThatWaitAfterwards()
	{
		PipelineActivity activity = new();
		Task first = activity.NextChangeAsync();
		Task second = activity.NextChangeAsync();

		activity.NotifyCardsChanged();
		Task later = activity.NextChangeAsync();

		await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		Assert.False(later.IsCompleted, "A waiter that starts after the announcement waits for the next one.");
	}
}
