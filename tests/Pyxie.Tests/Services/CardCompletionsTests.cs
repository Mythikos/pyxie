using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.ReadModels;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardCompletionsTests
{
	[Fact]
	public async Task CompletionRangeUsesMergeTimeEvenWhenTheCardWasClosedDaysLater()
	{
		await using PipelineRig rig = new();
		WorkCard card = await rig.CreateCardAsync("Merged");
		await rig.CompleteAsync(await rig.ClaimAsync());
		DateTime merged = rig.Database.Clock.UtcNow;
		rig.Database.Clock.Advance(TimeSpan.FromDays(3));
		WorkCard queued = await rig.CreateCardAsync("Still queued");
		Assert.Equal(1, await rig.Cards.CloseCompletedAsync(TestContext.Current.CancellationToken));
		Assert.Equal(0, await rig.Cards.CloseCompletedAsync(TestContext.Current.CancellationToken));
		Assert.Equal(CardLane.Queue, ResultAssert.Succeeded(await rig.Cards.GetAsync(queued.Id, TestContext.Current.CancellationToken)).Lane);
		Assert.Equal(new CompletedCard(card.Id, card.Name, CardLane.Closed, merged), Assert.Single(await rig.Cards.GetCompletedAsync(merged, merged.AddDays(1), 0, 10, TestContext.Current.CancellationToken)));
		Assert.Empty(await rig.Cards.GetCompletedAsync(merged.AddDays(-1), merged, 0, 10, TestContext.Current.CancellationToken));
		Assert.Empty(await rig.Cards.GetCompletedAsync(merged.AddDays(1), merged.AddDays(4), 0, 10, TestContext.Current.CancellationToken));
	}
}
