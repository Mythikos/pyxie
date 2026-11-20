using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Media;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Media;

public sealed class CardMediaTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Save_AttachesOnlySelectedFilesInTheCardTransaction()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		MediaFile selected = await rig.UploadAsync(session);
		MediaFile omitted = await rig.UploadAsync(session, "omit");
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [selected.Id]) }, Token));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		MediaFile file = Assert.Single(await context.MediaFiles.ToListAsync(Token));
		Assert.Equal(card.Id, file.CardId);
		Assert.Null(file.UploadId);
		Assert.Empty(await context.MediaUploads.ToListAsync(Token));
		await rig.Maintenance.SweepAsync(rig.Project, Token);
		Assert.True(File.Exists(MediaPaths.Object(rig.Folder, selected.Id)));
		Assert.False(File.Exists(MediaPaths.Object(rig.Folder, omitted.Id)));
	}

	[Fact]
	public async Task InvalidSelection_RollsBackCardCreationAndPreservesUploadForRetry()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		MediaFile file = await rig.UploadAsync(session);
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		ResultAssert.Failed(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [file.Id, Guid.NewGuid()]) }, Token));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.Empty(await context.Cards.ToListAsync(Token));
		Assert.Equal(session.UploadId, (await context.MediaFiles.SingleAsync(Token)).UploadId);
	}

	[Fact]
	public async Task StaleSave_DoesNotOverwriteFieldsOrAttachFiles()
	{
		await using MediaRig rig = new();
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Original");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input, Token));
		CardMediaSession session = await rig.BeginAsync(card);
		MediaFile file = await rig.UploadAsync(session);
		ResultAssert.Succeeded(await rig.Pipeline.Cards.UpdateAsync(card.Id, input with { Name = "Other editor" }, Token));
		ResultAssert.Failed(await rig.Pipeline.Cards.UpdateAsync(card.Id, input with { ExpectedVersion = card.Version, Media = new MediaEdit(session.UploadId, [file.Id]) }, Token));
		Assert.Equal("Other editor", (await TestData.FindCardAsync(rig.Pipeline.Database, card.Id))?.Name);
		Assert.Empty(ResultAssert.Succeeded(await rig.Cards.ListAsync(1, card.Id, Token)));
	}

	[Fact]
	public async Task PipelineClaim_FreezesContextAndRefusesFurtherUploads()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		MediaFile file = await rig.UploadAsync(session);
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [file.Id]) }, Token));
		CardMediaSession edit = await rig.BeginAsync(card);
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		using MemoryStream content = new([1, 2, 3]);
		ResultAssert.Failed(await rig.Storage.UploadAsync(rig.Project, edit.UploadId, "late.txt", content, Token));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.Equal(file.Id, (await context.RoundMediaInputs.SingleAsync(Token)).MediaFileId);
		Assert.Contains("/pyxie/in/media/manifest.json", work.Briefing, StringComparison.Ordinal);
		Assert.Contains("/pyxie/out/media/manifest.json", work.Briefing, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(RoundResult.Succeeded)]
	[InlineData(RoundResult.Errored)]
	[InlineData(RoundResult.Blocked)]
	public async Task FinalizingWork_PreservesOutputForEveryOutcome(RoundResult result)
	{
		await using MediaRig rig = new();
		await TestData.AddCardAsync(rig.Pipeline.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		MediaFile file = new() { Id = Guid.NewGuid(), CardId = work.CardId, RoundId = work.RoundId, PublicationKey = "files/diagnostic.txt", Name = "diagnostic.txt", Length = 3, Sha256 = new string('A', 64), CreatedUtc = rig.Pipeline.Database.Clock.UtcNow };
		ResultAssert.SucceededWithOptionalValue(await rig.Pipeline.Pipeline.RecordResultAsync(work.RoundId, result, new RoundReport("Result", "Details") { Media = [file] }, Token));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.Equal(file.Id, (await context.MediaFiles.SingleAsync(Token)).Id);
		if (result == RoundResult.Succeeded)
		{
			Assert.Equal(file.Id, (await context.RoundMediaInputs.SingleAsync(Token)).MediaFileId);
		}
	}

	[Fact]
	public async Task Interrupt_PersistsOutputAndResumeReceivesIt()
	{
		await using MediaRig rig = new();
		await TestData.AddCardAsync(rig.Pipeline.Database, "Card");
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		MediaFile file = new() { Id = Guid.NewGuid(), CardId = work.CardId, RoundId = work.RoundId, PublicationKey = "files/capture.png", Name = "capture.png", Length = 3, Sha256 = new string('A', 64), CreatedUtc = rig.Pipeline.Database.Clock.UtcNow };
		ResultAssert.Succeeded(await rig.Pipeline.Pipeline.InterruptAsync(work.RoundId, new RoundReport("Stopped", null) { Media = [file] }, Token));
		PipelineWork resumed = await rig.Pipeline.ClaimAsync();
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		RoundMediaInput snapshot = await context.RoundMediaInputs.SingleAsync(Token);
		Assert.Equal(resumed.RoundId, snapshot.RoundId);
		Assert.Equal(file.Id, snapshot.MediaFileId);
	}

	[Fact]
	public async Task RemovingUsedContext_RetainsHistoryAndRestartsStoppedReviewAtWork()
	{
		await using MediaRig rig = new();
		CardMediaSession initial = await rig.BeginAsync();
		MediaFile file = await rig.UploadAsync(initial);
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card", reviewerAgentId: TestData.DefaultAgent);
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(initial.UploadId, [file.Id]) }, Token));
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		PipelineWork? review = ResultAssert.SucceededWithOptionalValue(await rig.Pipeline.Pipeline.RecordResultAsync(work.RoundId, RoundResult.Succeeded, new RoundReport(null, null), Token));
		Assert.NotNull(review);
		Assert.Equal(RoundKind.Review, review.Kind);
		ResultAssert.SucceededWithOptionalValue(await rig.Pipeline.Pipeline.RecordResultAsync(review.RoundId, RoundResult.Blocked, new RoundReport("Needs updated context", null), Token));
		WorkCard stopped = ResultAssert.Succeeded(await rig.Pipeline.Cards.GetAsync(card.Id, Token));
		Assert.Equal(RoundKind.Review, stopped.ResumeRound);
		CardMediaSession edit = await rig.BeginAsync(stopped);
		WorkCard edited = ResultAssert.Succeeded(await rig.Pipeline.Cards.UpdateAsync(card.Id, input with { ExpectedVersion = stopped.Version, Media = new MediaEdit(edit.UploadId, []) }, Token));
		Assert.Null(edited.ResumeRound);
		await rig.Maintenance.SweepAsync(rig.Project, Token);
		Assert.True(File.Exists(MediaPaths.Object(rig.Folder, file.Id)));
		Assert.NotNull(Assert.Single(ResultAssert.Succeeded(await rig.Cards.ListAsync(1, card.Id, Token))).RemovedUtc);
		ResultAssert.Succeeded(await rig.Pipeline.Cards.RetryAsync(card.Id, Token));
		PipelineWork resumed = await rig.Pipeline.ClaimAsync();
		Assert.Equal(RoundKind.Work, resumed.Kind);
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.True(await context.RoundMediaInputs.AnyAsync(item => item.RoundId == work.RoundId && item.MediaFileId == file.Id, Token));
		Assert.True(await context.RoundMediaInputs.AnyAsync(item => item.RoundId == review.RoundId && item.MediaFileId == file.Id, Token));
		Assert.False(await context.RoundMediaInputs.AnyAsync(item => item.RoundId == resumed.RoundId, Token));
	}

	[Fact]
	public async Task CancelAndExpiration_ReclaimOnlyUnreferencedObjects()
	{
		await using MediaRig rig = new();
		CardMediaSession cancelled = await rig.BeginAsync();
		CardMediaSession expired = await rig.BeginAsync();
		await rig.UploadAsync(cancelled);
		await rig.UploadAsync(expired);
		await rig.Cards.AbandonAsync(1, cancelled.UploadId, Token);
		rig.Pipeline.Database.Clock.Advance(TimeSpan.FromDays(2));
		await rig.Maintenance.SweepAsync(rig.Project, Token);
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "objects")));
	}
}
