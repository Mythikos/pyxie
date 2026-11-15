using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;

using Xunit;

namespace Pyxie.Tests.Support;

/// <summary>Reads the stored repository rows directly through the context, so repository service tests verify state independently of the service.</summary>
public static class RepositoryTestData
{
	/// <summary>Reads every stored repository without tracking. There is at most one while the service's guarantee holds.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The repositories, oldest first.</returns>
	public static async Task<List<Repository>> ReadAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.Repositories
			.AsNoTracking()
			.OrderBy(repository => repository.Id)
			.ToListAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Reads the only stored repository and asserts that there is exactly one.</summary>
	/// <param name="database">The database to read from.</param>
	/// <returns>The stored repository.</returns>
	public static async Task<Repository> ReadSingleAsync(TestDatabase database)
	{
		return Assert.Single(await ReadAsync(database));
	}
}
