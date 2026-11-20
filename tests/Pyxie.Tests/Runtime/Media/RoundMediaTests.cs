using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Media;

public sealed class RoundMediaTests
{
	private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task HandIn_CopiesExactSnapshotAndDetectsChangedObjects()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		MediaFile file = await rig.UploadAsync(session, "original bytes");
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [file.Id]) }, Token));
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		using ProcessRunner runner = new(TimeProvider.System);
		SandboxSpec spec = new("test", "test", "test", work.CardId, SandboxRole.Worker, Path.Combine(rig.Folder, "in"), Path.Combine(rig.Folder, "out"));
		await using LocalSandbox sandbox = new(runner, spec, Path.Combine(rig.Folder, "work"));
		await rig.Rounds.HandInAsync(rig.Project, work.RoundId, sandbox, Token);
		Assert.Equal("original bytes", await File.ReadAllTextAsync(Path.Combine(spec.InputFolder, "media", "files", file.Id.ToString("N")), Token));
		await File.WriteAllTextAsync(MediaPaths.Object(rig.Folder, file.Id), "tampered bytes", Token);
		await Assert.ThrowsAsync<SandboxException>(() => rig.Rounds.HandInAsync(rig.Project, work.RoundId, sandbox, Token));
	}

	[Fact]
	public async Task Collection_IsDurableAndIdempotentAndIgnoresUnpublishedFiles()
	{
		await using MediaRig rig = new();
		string output = await PublishAsync(rig, [new() { Path = "files/result.txt", Name = "result.txt", Caption = "Useful" }]);
		await File.WriteAllTextAsync(Path.Combine(output, "media", "files", "unpublished.txt"), "private", Token);
		MediaCollection first = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, "abc", Token);
		MediaFile file = Assert.Single(first.Files);
		Assert.Empty(first.Problems);
		Assert.Equal("abc", file.Commit);
		Assert.Equal(Encoding.UTF8.GetBytes("result"), await File.ReadAllBytesAsync(MediaPaths.Object(rig.Folder, file.Id), Token));
		Assert.Equal(Convert.ToHexString(SHA256.HashData("result"u8)), file.Sha256);
		MediaCollection second = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		Assert.Equal(file.Id, Assert.Single(second.Files).Id);
		Assert.Single(Directory.EnumerateFiles(Path.Combine(MediaPaths.Root(rig.Folder), "objects")));
	}

	[Theory]
	[InlineData("../secret")]
	[InlineData("/etc/passwd")]
	[InlineData("files/../../secret")]
	[InlineData("files\\result.txt")]
	[InlineData("files/result.txt:stream")]
	[InlineData("files/./result.txt")]
	[InlineData("files/result.txt.")]
	public async Task Collection_RejectsEscapesAndAmbiguousPaths(string path)
	{
		await using MediaRig rig = new();
		string output = await PublishAsync(rig, [new() { Path = path, Name = "result.txt" }]);
		MediaCollection result = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		Assert.Empty(result.Files);
		Assert.NotEmpty(result.Problems);
	}

	[Fact]
	public async Task Collection_RejectsDuplicatePathsAndDirectorySourcesButKeepsGoodFiles()
	{
		await using MediaRig rig = new();
		string output = await PublishAsync(rig, [new() { Path = "files/result.txt", Name = "result.txt" }, new() { Path = "files/result.txt", Name = "duplicate.txt" }, new() { Path = "files/folder", Name = "folder" }]);
		Directory.CreateDirectory(Path.Combine(output, "media", "files", "folder"));
		MediaCollection result = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		Assert.Single(result.Files);
		Assert.Equal(2, result.Problems.Count);
	}

	[Fact]
	public async Task Collection_ResumesAPartialReceiptWithoutDuplicatingItsObjects()
	{
		await using MediaRig rig = new();
		string output = await PublishAsync(rig, [new() { Path = "files/result.txt", Name = "first.txt" }]);
		MediaCollection first = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		await File.WriteAllTextAsync(MediaPaths.Receipt(rig.Folder, 2), JsonSerializer.Serialize(first with { Complete = false }, s_json), Token);
		await PublishAsync(rig, [new() { Path = "files/result.txt", Name = "first.txt" }, new() { Path = "files/second.txt", Name = "second.txt" }]);
		await File.WriteAllTextAsync(Path.Combine(output, "media", "files", "second.txt"), "second", Token);
		MediaCollection resumed = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		Assert.Equal(2, resumed.Files.Count);
		Assert.Equal(first.Files[0].Id, resumed.Files[0].Id);
		Assert.True(resumed.Complete);
	}

	[Fact]
	public async Task Maintenance_KeepsReceiptObjectsUntilRoundMetadataCommits()
	{
		await using MediaRig rig = new();
		await TestData.AddCardAsync(rig.Pipeline.Database, "Card");
		PipelineWork work = await rig.Pipeline.ClaimAsync();
		string output = await PublishAsync(rig, [new() { Path = "files/result.txt", Name = "result.txt" }]);
		MediaCollection collected = await rig.Rounds.CollectAsync(rig.Project, work.CardId, work.RoundId, output, null, Token);
		await rig.Maintenance.SweepAsync(rig.Project, Token);
		Assert.True(File.Exists(MediaPaths.Object(rig.Folder, collected.Files[0].Id)));
		ResultAssert.Succeeded(await rig.Pipeline.Pipeline.InterruptAsync(work.RoundId, new RoundReport("Stopped", null) { Media = collected.Files }, Token));
		await rig.Maintenance.SweepAsync(rig.Project, Token);
		Assert.False(File.Exists(MediaPaths.Receipt(rig.Folder, work.RoundId)));
		Assert.True(File.Exists(MediaPaths.Object(rig.Folder, collected.Files[0].Id)));
		await using ProjectDbContext context = rig.Pipeline.Database.CreateContext();
		Assert.Single(await context.MediaFiles.ToListAsync(Token));
	}

	[Theory]
	[InlineData("{\"version\":2,\"files\":[]}")]
	[InlineData("{\"version\":1,\"files\":null}")]
	[InlineData("not json")]
	public async Task Collection_MalformedManifestReturnsAProblemWithoutThrowing(string manifest)
	{
		await using MediaRig rig = new();
		string output = await PublishAsync(rig, []);
		await File.WriteAllTextAsync(Path.Combine(output, "media", "manifest.json"), manifest, Token);
		MediaCollection result = await rig.Rounds.CollectAsync(rig.Project, 1, 2, output, null, Token);
		Assert.Empty(result.Files);
		Assert.NotEmpty(result.Problems);
	}

	private static async Task<string> PublishAsync(MediaRig rig, IReadOnlyList<MediaManifestEntry> entries)
	{
		string output = Path.Combine(rig.Folder, "out");
		Directory.CreateDirectory(Path.Combine(output, "media", "files"));
		await File.WriteAllTextAsync(Path.Combine(output, "media", "files", "result.txt"), "result", Token);
		await File.WriteAllTextAsync(Path.Combine(output, "media", "manifest.json"), JsonSerializer.Serialize(new MediaManifest { Files = entries }, s_json), Token);
		return output;
	}
}
