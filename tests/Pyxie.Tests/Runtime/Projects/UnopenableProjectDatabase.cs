using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Projects;

/// <summary>Creates an unusable scratch database just before the real provider migrates it, to exercise import cleanup after a database refusal.</summary>
internal sealed class UnopenableProjectDatabase(bool fromAnotherBuild) : IProjectDatabase
{
	public bool FailMigration { get; set; } = true;

	public async Task<ServiceResult> MigrateAsync(string folder, CancellationToken cancellationToken)
	{
		SqliteProjectDatabase database = new();
		if (FailMigration)
		{
			ResultAssert.Succeeded(await database.MigrateAsync(folder, cancellationToken));
			await ProjectDatabaseFile.MakeUnopenableAsync(folder, fromAnotherBuild, cancellationToken);
		}

		return await database.MigrateAsync(folder, cancellationToken);
	}
}
