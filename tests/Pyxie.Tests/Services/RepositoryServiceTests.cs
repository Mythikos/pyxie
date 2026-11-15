using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class RepositoryServiceTests
{
	private const string AlreadyConnected = "A repository is already connected.";

	[Fact]
	public async Task GetAsync_IsNullUntilARepositoryIsConnected()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);

		Assert.Null(await service.GetAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task GetAsync_ReturnsTheConnectedRepository()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		Repository connected = ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Url(targetBranch: "develop"), token));

		Repository? found = await service.GetAsync(token);

		Assert.NotNull(found);
		Assert.Equal(connected.Id, found.Id);
		Assert.Equal(RepositorySource.Url, found.Source);
		Assert.Equal(RepositoryTestInput.DefaultUrl, found.Origin);
		Assert.Equal("develop", found.TargetBranch);
	}

	[Fact]
	public async Task ConnectAsync_StoresAFolderImportWithItsClonePathAndOrigin()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		string path = RepositoryTestInput.PathFor("my-app");
		string folder = RepositoryTestInput.PathFor("my-app-source");

		Repository connected = ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(path, "trunk", folder), TestContext.Current.CancellationToken));

		Repository stored = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(connected.Id, stored.Id);
		Assert.Equal(RepositorySource.Folder, stored.Source);
		Assert.Equal(path, stored.Path);
		Assert.Equal(folder, stored.Origin);
		Assert.Equal("trunk", stored.TargetBranch);
	}

	[Fact]
	public async Task ConnectAsync_StoresAUrlImportWithItsClonePathAndOrigin()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		string path = RepositoryTestInput.PathFor("cloned-app");

		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Url(path: path, origin: "https://example.com/other/app.git"), TestContext.Current.CancellationToken));

		Repository stored = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(RepositorySource.Url, stored.Source);
		Assert.Equal(path, stored.Path);
		Assert.Equal("https://example.com/other/app.git", stored.Origin);
	}

	[Fact]
	public async Task ConnectAsync_StampsTheTimeFromTheClockInUtc()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		DateTime connectedAt = database.Clock.Advance(TimeSpan.FromHours(5));

		Repository connected = ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), TestContext.Current.CancellationToken));

		Assert.Equal(connectedAt, connected.ImportedUtc);
		Repository stored = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(connectedAt, stored.ImportedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.ImportedUtc.Kind);
	}

	[Fact]
	public async Task ConnectAsync_TrimsPathOriginAndBranch()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		string path = RepositoryTestInput.PathFor("trimmed");
		RepositoryInput input = new(RepositorySource.Url, $"  {path}\t", $" {RepositoryTestInput.DefaultUrl} \r\n", "  main  ");

		ResultAssert.Succeeded(await service.ConnectAsync(input, TestContext.Current.CancellationToken));

		Repository stored = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(path, stored.Path);
		Assert.Equal(RepositoryTestInput.DefaultUrl, stored.Origin);
		Assert.Equal("main", stored.TargetBranch);
	}

	[Theory]
	[InlineData(RepositorySource.Folder, "", null, "main", "Path is required.")]
	[InlineData(RepositorySource.Folder, "   ", null, "main", "Path is required.")]
	[InlineData(RepositorySource.Folder, "relative/repo", null, "main", "Path must be an absolute path.")]
	[InlineData(RepositorySource.Url, "..", RepositoryTestInput.DefaultUrl, "main", "Path must be an absolute path.")]
	[InlineData(RepositorySource.Folder, null, "", "main", "Folder is required.")]
	[InlineData(RepositorySource.Folder, null, "   ", "main", "Folder is required.")]
	[InlineData(RepositorySource.Folder, null, "relative/source", "main", "Folder must be a full path.")]
	[InlineData(RepositorySource.Folder, null, RepositoryTestInput.DefaultUrl, "main", "Folder must be a full path.")]
	[InlineData(RepositorySource.Url, null, "", "main", "URL is required.")]
	[InlineData(RepositorySource.Url, null, "   ", "main", "URL is required.")]
	[InlineData(RepositorySource.Folder, null, null, "", "Target branch is required.")]
	[InlineData(RepositorySource.Url, null, RepositoryTestInput.DefaultUrl, "  ", "Target branch is required.")]
	[InlineData(RepositorySource.Folder, null, null, "my branch", "Target branch cannot contain whitespace.")]
	[InlineData((RepositorySource)2, null, null, "main", "Source is not recognized.")]
	[InlineData((RepositorySource)(-1), null, RepositoryTestInput.DefaultUrl, "main", "Source is not recognized.")]
	public async Task ConnectAsync_RejectsInvalidFieldsWithTheRuleMessage_AndStoresNothing(RepositorySource source, string? path, string? origin, string targetBranch, string expected)
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		RepositoryInput input = new(source, path ?? RepositoryTestInput.PathFor("valid"), origin ?? ValidOrigin(source), targetBranch);

		string error = ResultAssert.Failed(await service.ConnectAsync(input, TestContext.Current.CancellationToken));

		Assert.Equal(expected, error);
		Assert.Empty(await RepositoryTestData.ReadAsync(database));
	}

	[Fact]
	public async Task ConnectAsync_ReportsTheFirstInvalidField_InTheOrderTheFormShowsThem()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		RepositoryInput everythingWrong = new((RepositorySource)2, "relative", "relative-origin", "two words");
		RepositoryInput folderImport = everythingWrong with { Source = RepositorySource.Folder };

		Assert.Equal("Source is not recognized.", ResultAssert.Failed(await service.ConnectAsync(everythingWrong, token)));
		Assert.Equal("Path must be an absolute path.", ResultAssert.Failed(await service.ConnectAsync(folderImport, token)));
		Assert.Equal("Folder must be a full path.", ResultAssert.Failed(await service.ConnectAsync(folderImport with { Path = RepositoryTestInput.PathFor("valid") }, token)));
		Assert.Equal("Target branch cannot contain whitespace.", ResultAssert.Failed(await service.ConnectAsync(folderImport with { Path = RepositoryTestInput.PathFor("valid"), Origin = RepositoryTestInput.PathFor("source-folder") }, token)));
	}

	[Fact]
	public async Task ConnectAsync_RefusesASecondRepository_AndKeepsTheFirstUnchanged()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), token));
		Repository before = await RepositoryTestData.ReadSingleAsync(database);
		database.Clock.Tick();

		string error = ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Url(RepositoryTestInput.PathFor("second"), "develop"), token));

		Assert.Equal(AlreadyConnected, error);
		Repository after = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(before.Id, after.Id);
		Assert.Equal(before.Path, after.Path);
		Assert.Equal(before.TargetBranch, after.TargetBranch);
		Assert.Equal(before.ImportedUtc, after.ImportedUtc);
	}

	[Fact]
	public async Task ConnectAsync_RefusesTheSameInputAgain()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), token));

		Assert.Equal(AlreadyConnected, ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Folder(), token)));
		Assert.Single(await RepositoryTestData.ReadAsync(database));
	}

	[Fact]
	public async Task ConnectAsync_RefusesWithoutStartingATransactionWhenARepositoryIsAlreadyConnected()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), token));
		bool transactionStarted = false;
		interceptor.Arm(() =>
		{
			transactionStarted = true;

			return Task.CompletedTask;
		});

		ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Folder(RepositoryTestInput.PathFor("second")), token));

		Assert.False(transactionStarted, "A refusal that needs no write must not take the database's write lock.");
	}

	[Fact]
	public async Task ConnectAsync_RefusesWhenARepositoryWasConnectedOutsideTheService()
	{
		await using TestDatabase database = new();
		TestData.ConnectRepository(database);
		RepositoryService service = new(database.ContextFactory, database.Clock);

		string error = ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Folder(RepositoryTestInput.PathFor("second")), TestContext.Current.CancellationToken));

		Assert.Equal(AlreadyConnected, error);
		Assert.Equal(TestData.RepositoryPath, (await RepositoryTestData.ReadSingleAsync(database)).Path);
	}

	[Fact]
	public async Task ConnectAsync_ChecksTheInputBeforeLookingForAConnectedRepository()
	{
		await using TestDatabase database = new();
		RepositoryService service = new(database.ContextFactory, database.Clock);
		CancellationToken token = TestContext.Current.CancellationToken;
		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), token));

		string error = ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Folder(path: "relative"), token));

		Assert.Equal("Path must be an absolute path.", error);
	}

	private static string ValidOrigin(RepositorySource source)
	{
		return source == RepositorySource.Folder ? RepositoryTestInput.PathFor("source-folder") : RepositoryTestInput.DefaultUrl;
	}
}
