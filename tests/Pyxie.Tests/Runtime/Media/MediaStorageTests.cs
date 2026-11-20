using System.Text;

using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Runtime.Media;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Media;

public sealed class MediaStorageTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task SlowUpload_DoesNotBlockOtherUploadsOrMaintenanceAndClosingCancelsIt()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		using HoldingUploadStream slow = new();
		Task<Pyxie.Data.Services.Results.ServiceResult<MediaFile>> receiving = rig.Storage.UploadAsync(rig.Project, session.UploadId, "slow.txt", slow, Token);
		await slow.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
		await rig.UploadAsync(session, "other").WaitAsync(TimeSpan.FromSeconds(10), Token);
		await rig.Maintenance.SweepAsync(rig.Project, Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
		Assert.Single(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "staging"), "*.part"));
		rig.Open.Set(null);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
		await rig.Storage.DrainUploadsAsync(rig.Folder, Token);
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "staging"), "*.part"));
	}

	[Fact]
	public async Task Limits_CountActualBytesAndReleaseFailedWrites()
	{
		await using MediaRig rig = new(new MediaOptions { MaxFileBytes = 8, MaxBatchBytes = 10, MaxProjectBytes = 12 });
		CardMediaSession session = await rig.BeginAsync();
		await rig.UploadAsync(session, "12345678");
		using MemoryStream exceedsBatch = new("123"u8.ToArray());
		ResultAssert.Failed(await rig.Storage.UploadAsync(rig.Project, session.UploadId, "extra.txt", exceedsBatch, Token));
		CardMediaSession another = await rig.BeginAsync();
		using MemoryStream exceedsProject = new("12345"u8.ToArray());
		ResultAssert.Failed(await rig.Storage.UploadAsync(rig.Project, another.UploadId, "extra.txt", exceedsProject, Token));
		using MemoryStream exceedsFile = new("123456789"u8.ToArray());
		ResultAssert.Failed(await rig.Storage.UploadAsync(rig.Project, another.UploadId, "extra.txt", exceedsFile, Token));
		Assert.Single(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "objects")));
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "staging")));
	}

	[Fact]
	public async Task ProjectSwitch_RejectsAnOldSessionWithoutWritingIntoEitherProject()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		rig.Open.Set(new Project { Id = 2, Folder = Path.Combine(rig.Folder, "other") });
		using MemoryStream content = new("bytes"u8.ToArray());
		ResultAssert.Failed(await rig.Storage.UploadAsync(rig.Project, session.UploadId, "context.txt", content, Token));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.Empty(await context.MediaFiles.ToListAsync(Token));
		Assert.False(Directory.Exists(MediaPaths.Root(rig.Folder)));
	}

	[Theory]
	[InlineData("<svg onload='alert(1)'/>")]
	[InlineData("<html><script>alert(1)</script></html>")]
	[InlineData("%PDF-1.7")]
	public void UntrustedDocuments_AreOpaqueDownloads(string content)
	{
		Assert.Equal("application/octet-stream", MediaContent.Detect(Encoding.UTF8.GetBytes(content)));
	}

	[Theory]
	[InlineData("../secret")]
	[InlineData("name:stream")]
	[InlineData("file.txt.")]
	[InlineData("name\n.txt")]
	[InlineData("")]
	[InlineData(null)]
	public void InvalidNames_AreRefused(string? name) => Assert.False(MediaContent.ValidName(name));
}
