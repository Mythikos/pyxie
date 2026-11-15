using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The interceptor-driven race tests prove the service's logic; this one proves it against real locking, with callers running on their own connections at the same time.</summary>
public sealed class RepositoryServiceConnectConcurrencyTests
{
	private const int Callers = 8;

	[Fact]
	public async Task ConnectAsync_LetsExactlyOneCallerWin_WhenManyConnectAtOnce()
	{
		await using RepositoryRaceDatabase database = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int[] callers = [.. Enumerable.Range(1, Callers)];

		Task<ServiceResult<Repository>>[] calls =
		[
			.. callers.Select(caller => Task.Run(
				async () =>
				{
					RepositoryService service = new(database.ContextFactory, TimeProvider.System);
					await start.Task;

					return await service.ConnectAsync(RepositoryTestInput.Folder(RepositoryTestInput.PathFor($"caller-{caller}")), token);
				},
				token)),
		];
		start.SetResult();
		ServiceResult<Repository>[] results = await Task.WhenAll(calls);

		ServiceResult<Repository> winner = Assert.Single(results, result => result.Succeeded);
		Assert.All(results.Where(result => !result.Succeeded), result => Assert.Equal("A repository is already connected.", result.Error));
		await using ProjectDbContext context = database.ContextFactory.CreateDbContext();
		Repository stored = await context.Repositories.AsNoTracking().SingleAsync(token);
		Assert.NotNull(winner.Value);
		Assert.Equal(winner.Value.Path, stored.Path);
	}
}
