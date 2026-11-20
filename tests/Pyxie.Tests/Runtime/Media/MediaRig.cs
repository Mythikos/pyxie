using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Media;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Media;

internal sealed class MediaRig : IAsyncDisposable
{
	public MediaRig(MediaOptions? options = null)
	{
		Folder = TempFolder.Create("pyxie-media");
		Project = new Project { Id = 1, Name = "Media", Folder = Folder };
		Open.Set(Project);
		Storage = new MediaStorage(Open, Pipeline.Database.ContextSource, Pipeline.Database.Clock, Options.Create(options ?? new MediaOptions()));
		Cards = new CardMediaService(Open, Pipeline.Database.ContextSource, Pipeline.Database.Clock);
		Rounds = new RoundMedia(Pipeline.Database.ContextSource, Storage);
		Maintenance = new MediaMaintenance(Open, Pipeline.Database.ContextSource, Storage, Pipeline.Database.Clock, NullLogger<MediaMaintenance>.Instance);
	}

	public string Folder { get; }
	public Project Project { get; }
	public OpenProject Open { get; } = new();
	public PipelineRig Pipeline { get; } = new();
	public MediaStorage Storage { get; }
	public CardMediaService Cards { get; }
	public RoundMedia Rounds { get; }
	public MediaMaintenance Maintenance { get; }
	public static CancellationToken Token => TestContext.Current.CancellationToken;

	public async Task<CardMediaSession> BeginAsync(WorkCard? card = null) => ResultAssert.Succeeded(await Cards.BeginAsync(Project.Id, card?.Id, card?.Version, Token));

	public async Task<MediaFile> UploadAsync(CardMediaSession session, string content = "context", string name = "context.txt")
	{
		using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));
		return ResultAssert.Succeeded(await Storage.UploadAsync(Project, session.UploadId, name, stream, Token));
	}

	public async ValueTask DisposeAsync()
	{
		Maintenance.Dispose();
		Storage.Dispose();
		await Pipeline.DisposeAsync();
		await TempFolder.DeleteAsync(Folder);
	}
}
