using System.Text;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Services.Inputs;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Media;
using Pyxie.Tests.Support;
using Pyxie.Web.Mcp;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpMediaTests
{
	[Fact]
	public async Task MediaPagesPreserveTiedTimestampsAndOnlyExposeAttachmentMetadata()
	{
		await using MediaRig rig = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		CardMediaSession session = await rig.BeginAsync();
		MediaFile one = await rig.UploadAsync(session, "first", "one.txt");
		MediaFile two = await rig.UploadAsync(session, "second", "two.txt");
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Media");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [one.Id, two.Id]) }, token));
		MediaTools tools = new(new PipelineController(rig.Open), rig.Cards);

		JsonElement first = JsonSerializer.SerializeToElement(await tools.ListCardMediaAsync(rig.Project.Id, card.Id, token, count: 1));
		JsonElement firstFile = Assert.Single(first.GetProperty("Files").EnumerateArray());
		Guid cursor = first.GetProperty("NextAfterFileId").GetGuid();
		Assert.Equal(cursor, firstFile.GetProperty("Id").GetGuid());
		Assert.False(firstFile.TryGetProperty("PublicationKey", out _), "Internal producer storage keys must stay out of the listing.");
		Assert.False(firstFile.TryGetProperty("UploadId", out _), "Upload session identifiers must stay out of the listing.");
		JsonElement second = JsonSerializer.SerializeToElement(await tools.ListCardMediaAsync(rig.Project.Id, card.Id, token, 1, cursor));
		JsonElement last = Assert.Single(second.GetProperty("Files").EnumerateArray());
		Assert.Equal(JsonValueKind.Null, second.GetProperty("NextAfterFileId").ValueKind);
		Assert.Equal(new[] { one.Id, two.Id }.Order(), new[] { cursor, last.GetProperty("Id").GetGuid() }.Order());
		WorkCard other = await rig.Pipeline.CreateCardAsync("Other");
		await Assert.ThrowsAsync<McpException>(() => tools.ListCardMediaAsync(rig.Project.Id, other.Id, token, 1, cursor));
	}

	[Fact]
	public async Task MediaChunkReadsVerifyProjectCardAndFileOwnershipAndReturnExactBytes()
	{
		await using MediaRig rig = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		CardMediaSession session = await rig.BeginAsync();
		MediaFile file = await rig.UploadAsync(session, "0123456789");
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Media");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [file.Id]) }, token));
		WorkCard other = await rig.Pipeline.CreateCardAsync("Other card");
		MediaTools tools = new(new PipelineController(rig.Open), rig.Cards);
		CallToolResult chunk = await tools.ReadCardMediaAsync(rig.Project.Id, card.Id, file.Id, token, 6, 4);
		TextContentBlock progress = Assert.IsType<TextContentBlock>(chunk.Content[0]);
		Assert.Contains("nextOffset=10 length=10 EOF=True", progress.Text, StringComparison.Ordinal);
		BlobResourceContents blob = Assert.IsType<BlobResourceContents>(Assert.IsType<EmbeddedResourceBlock>(chunk.Content[1]).Resource);
		Assert.Equal("6789", Encoding.UTF8.GetString(blob.DecodedData.Span));
		await Assert.ThrowsAsync<McpException>(() => tools.ReadCardMediaAsync(rig.Project.Id, other.Id, file.Id, token));
		await Assert.ThrowsAsync<McpException>(() => tools.ReadCardMediaAsync(rig.Project.Id + 1, card.Id, file.Id, token));
		await Assert.ThrowsAsync<McpException>(() => tools.ReadCardMediaAsync(rig.Project.Id, card.Id, file.Id, token, 11));
	}
}
