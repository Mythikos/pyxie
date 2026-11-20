using System.ComponentModel;
using System.Globalization;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Mcp;

internal sealed class MediaTools(PipelineController pipeline, ICardMediaService media)
{
	private const int MaxChunk = 1024 * 1024;

	[McpServerTool(ReadOnly = true), Description("Page through card attachments and round outputs with file IDs, captions, types, lengths and SHA-256 hashes. Pass nextAfterFileId as afterFileId until it is null.")]
	public async Task<object> ListCardMediaAsync(int projectId, int cardId, CancellationToken cancellationToken, int count = 50, Guid? afterFileId = null)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		if (count is < 1 or > 100)
		{
			throw new McpException("Provide a count from 1 to 100.");
		}
		IReadOnlyList<MediaFile> page = (await media.ListAsync(projectId, cardId, cancellationToken, count + 1, afterFileId)).Value();
		return new
		{
			Files = page.Take(count).Select(file => new { file.Id, file.RoundId, file.Name, file.Caption, file.ContentType, file.Length, file.Sha256, file.Commit, file.CreatedUtc, file.RemovedUtc }).ToArray(),
			NextAfterFileId = page.Count > count ? page[count - 1].Id : (Guid?)null,
		};
	}

	[McpServerTool(ReadOnly = true), Description("Read a card attachment. Complete small images are returned as images. Other files return bounded binary chunks. Continue from nextOffset until EOF.")]
	public async Task<CallToolResult> ReadCardMediaAsync(int projectId, int cardId, Guid fileId, CancellationToken cancellationToken, long offset = 0, int count = MaxChunk)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		MediaFile file = (await media.GetAsync(projectId, cardId, fileId, cancellationToken)).Value();
		if (offset < 0 || offset > file.Length || count is < 1 or > MaxChunk)
		{
			throw new McpException("Provide an offset within the file and a count from 1 to 1048576.");
		}

		byte[] bytes = new byte[Math.Min(file.Length - offset, count)];
		try
		{
			await using FileStream stream = new(MediaPaths.Object(lease.Project.Folder, file.Id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous);
			stream.Position = offset;
			await stream.ReadExactlyAsync(bytes, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new McpException("The stored attachment could not be read.");
		}

		long nextOffset = offset + bytes.Length;
		bool whole = offset == 0 && nextOffset == file.Length;
		string uri = FormattableString.Invariant($"pyxie://projects/{projectId}/cards/{cardId}/media/{file.Id}?offset={offset}");
		ContentBlock content = whole && file.ContentType.StartsWith("image/", StringComparison.Ordinal)
			? ImageContentBlock.FromBytes(bytes, file.ContentType)
			: new EmbeddedResourceBlock { Resource = BlobResourceContents.FromBytes(bytes, uri, whole ? file.ContentType : "application/octet-stream") };
		return new CallToolResult { Content = [new TextContentBlock { Text = string.Create(CultureInfo.InvariantCulture, $"nextOffset={nextOffset} length={file.Length} EOF={nextOffset == file.Length}") }, content] };
	}
}
