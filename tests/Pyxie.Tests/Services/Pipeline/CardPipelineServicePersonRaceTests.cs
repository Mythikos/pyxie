using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>A person's edit or delete and a pipeline claim of the same card never both apply: whichever reaches the card second is refused or acts on the card as the first left it.</summary>
public sealed class CardPipelineServicePersonRaceTests
{
	[Fact]
	public async Task UpdateAsync_RefusesACardThatThePipelineClaimsAfterTheCheckAndLeavesItUntouched()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await rig.CreateCardAsync("Original", description: "Original text");
		CardInput edit = await TestCardInput.CreateAsync(rig.Database, "Renamed", description: "New text");
		PipelineWork? claimed = null;
		interceptor.Arm(async () => claimed = await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		string message = ResultAssert.Failed(await rig.Cards.UpdateAsync(card.Id, edit, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("This card is in In Progress and can no longer be edited.", message);
		Assert.NotNull(claimed);
		Assert.Equal(card.Id, claimed.CardId);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal("Original", stored.Name);
		Assert.Equal("Original text", stored.Description);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Equal(1, stored.Version);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
		Assert.NotNull(claimed.Briefing);
		Assert.Contains("Original text", claimed.Briefing, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DeleteAsync_RefusesACardThatThePipelineClaimsAfterTheCheckAndKeepsItWithItsRound()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card");
		interceptor.Arm(async () => await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken));

		string message = ResultAssert.Failed(await rig.Cards.DeleteAsync(card.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal("This card is in In Progress and can no longer be deleted.", message);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
	}

	[Fact]
	public async Task DeleteAsync_RefusesAStoppedCardThatIsRetriedAndClaimedAfterTheCheck()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using PipelineRig rig = new(interceptor);
		WorkCard card = await TestData.AddCardAsync(rig.Database, "Card", state: CardState.Error, attempt: 3);
		interceptor.Arm(async () =>
		{
			ResultAssert.Succeeded(await rig.Cards.RetryAsync(card.Id, TestContext.Current.CancellationToken));
			await rig.Pipeline.ClaimNextAsync(TestContext.Current.CancellationToken);
		});

		string message = ResultAssert.Failed(await rig.Cards.DeleteAsync(card.Id, TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Contains("can no longer be deleted", message, StringComparison.Ordinal);
		WorkCard? stored = await TestData.FindCardAsync(rig.Database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.InProgress, stored.Lane);
		Assert.Single(await TestData.ReadRoundsAsync(rig.Database, card.Id));
	}
}
