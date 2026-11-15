using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>
/// Two callers can connect at the same moment, each having checked that no repository exists, and no index tells their rows apart. Exactly one must win. The race is simulated by running a whole second connect just before the first one begins its transaction.
/// </summary>
public sealed class RepositoryServiceConnectRaceTests
{
	private const string AlreadyConnected = "A repository is already connected.";

	[Fact]
	public async Task ConnectAsync_RefusesWhenAnotherCallerConnectsAfterTheCheck()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		RepositoryService service = new(database.ContextFactory, database.Clock);
		RepositoryService other = new(database.ContextFactory, database.Clock);
		string otherPath = RepositoryTestInput.PathFor("won-the-race");
		interceptor.Arm(async () => ResultAssert.Succeeded(await other.ConnectAsync(RepositoryTestInput.Url(otherPath), TestContext.Current.CancellationToken)));

		string error = ResultAssert.Failed(await service.ConnectAsync(RepositoryTestInput.Folder(), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(AlreadyConnected, error);
		Repository stored = await RepositoryTestData.ReadSingleAsync(database);
		Assert.Equal(RepositorySource.Url, stored.Source);
		Assert.Equal(otherPath, stored.Path);
	}

	[Fact]
	public async Task ConnectAsync_StillConnectsWhenNoOtherCallerRaces()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		RepositoryService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm(() => Task.CompletedTask);

		ResultAssert.Succeeded(await service.ConnectAsync(RepositoryTestInput.Folder(), TestContext.Current.CancellationToken));

		interceptor.AssertFired();
		Assert.Equal(RepositoryTestInput.PathFor("folder-clone"), (await RepositoryTestData.ReadSingleAsync(database)).Path);
	}
}
