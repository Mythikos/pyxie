using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Reads the sandbox settings rows a test database holds, straight from the table, so a test sees exactly what a save wrote and not what the service makes of it.</summary>
public static class SandboxSettingsRows
{
	/// <summary>Reads every sandbox settings row.</summary>
	/// <param name="database">The database.</param>
	/// <returns>The rows, by identifier.</returns>
	public static async Task<List<SandboxSettings>> ReadAsync(TestDatabase database)
	{
		await using ProjectDbContext context = database.CreateContext();

		return await context.SandboxSettings.AsNoTracking().OrderBy(settings => settings.Id).ToListAsync(TestContext.Current.CancellationToken);
	}
}
