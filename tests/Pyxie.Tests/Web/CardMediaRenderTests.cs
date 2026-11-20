using System.Text.Json;

using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Runtime.Media;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardMediaRenderTests
{
	[Fact]
	public async Task Remove_DoubleClickDoesNotRemoveTheFileThatSlidesUnderThePointer()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		MediaFile first = await rig.UploadAsync(session, name: "first.txt");
		MediaFile second = await rig.UploadAsync(session, name: "second.txt");
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [first.Id, second.Id]) }, TestContext.Current.CancellationToken));
		await using ComponentHarness harness = new(services => services.AddSingleton<ICardMediaService>(rig.Cards));
		RenderedComponent<CardMedia> rendered = await harness.RenderAsync<CardMedia>(new Dictionary<string, object> { [nameof(CardMedia.Project)] = rig.Project, [nameof(CardMedia.Card)] = card });
		await rendered.ClickAsync("RemoveAsync", first, new MouseEventArgs { Detail = 1 });
		await rendered.ClickAsync("RemoveAsync", second, new MouseEventArgs { Detail = 2 });
		Assert.DoesNotContain("Remove first.txt", await rendered.HtmlAsync(), StringComparison.Ordinal);
		Assert.Contains("Remove second.txt", await rendered.HtmlAsync(), StringComparison.Ordinal);
		await rendered.RunAsync(media => Assert.Equal(second.Id, Assert.Single(media.Apply(input).Media!.FileIds)));
		await rendered.ClickAsync("RemoveAsync", second, new MouseEventArgs { Detail = 0 });
		await rendered.RunAsync(media => Assert.Empty(media.Apply(input).Media!.FileIds));
	}

	[Fact]
	public async Task UploadBatch_DisablesSaveUntilItsFailureIsAcknowledgedAndDisposalAbandonsTheSession()
	{
		await using MediaRig rig = new();
		await using ComponentHarness harness = new(services => services.AddSingleton<ICardMediaService>(rig.Cards));
		RenderedComponent<CardMedia> rendered = await harness.RenderAsync<CardMedia>(new Dictionary<string, object> { [nameof(CardMedia.Project)] = rig.Project });
		await rendered.RunAsync(async media =>
		{
			Assert.True(media.CanSave);
			Assert.True(await media.MediaUploadStarted());
			Assert.False(media.CanSave);
			media.MediaUploadFailed("capture.png", "Too large");
			await media.MediaUploadEnded();
			Assert.False(media.CanSave);
		});
		Assert.Contains("Dismiss failed upload", await rendered.HtmlAsync(), StringComparison.Ordinal);
		await rendered.ClickAsync("DismissFailureAsync");
		await rendered.RunAsync(media => { Assert.True(media.CanSave); return Task.CompletedTask; });
	}

	[Fact]
	public async Task ReadOnlyMedia_RejectsUploadCallbacksAndDoesNotOfferAttach()
	{
		await using MediaRig rig = new();
		await using ComponentHarness harness = new(services => services.AddSingleton<ICardMediaService>(rig.Cards));
		RenderedComponent<CardMedia> rendered = await harness.RenderAsync<CardMedia>(new Dictionary<string, object> { [nameof(CardMedia.Project)] = rig.Project, [nameof(CardMedia.ReadOnly)] = true });
		await rendered.RunAsync(async media =>
		{
			Assert.False(await media.MediaUploadStarted());
			media.MediaUploadCompleted(JsonSerializer.Serialize(new MediaFile { Id = Guid.NewGuid(), Name = "forged.png" }));
			Assert.False(media.HasFiles);
		});
		Assert.DoesNotContain("Attach files", await rendered.HtmlAsync(), StringComparison.Ordinal);
	}
}
