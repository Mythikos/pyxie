using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Workspaces;

namespace Pyxie.Web.Hosting;

/// <summary>Startup tasks for the web application.</summary>
internal static partial class PyxieWebApplicationExtensions
{
	/// <summary>Routes operator pages and local MCP requests. A page error reexecutes routing to render the Not Found page.</summary>
	/// <param name="app">The application.</param>
	public static void UsePyxieRouting(this WebApplication app)
	{
		app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
		app.UseRouting();
		app.Use(PyxieMcpHosting.GuardAsync);
	}

	/// <summary>Brings the machine database up to date and opens the project it records as open: when that project's folder, database, or hub is gone, or its database cannot be brought up to date, the project is closed and a warning logged; otherwise it becomes <see cref="OpenProject.Current"/>.</summary>
	/// <param name="app">The application.</param>
	/// <returns>A task that completes when the databases are ready.</returns>
	public static async Task OpenPyxieDatabasesAsync(this WebApplication app)
	{
		// SQLite creates the database file but not the folders above it.
		Directory.CreateDirectory(app.Services.GetRequiredService<WorkspaceLayout>().Root);

		IDbContextFactory<MachineDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<MachineDbContext>>();
		await using (MachineDbContext context = await factory.CreateDbContextAsync())
		{
			await context.Database.MigrateAsync();
		}

		await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
		IProjectCatalog catalog = scope.ServiceProvider.GetRequiredService<IProjectCatalog>();

		if (await catalog.GetOpenAsync(CancellationToken.None) is Project open)
		{
			if (ProjectSessions.IsFolderPresent(open))
			{
				ServiceResult migration = await scope.ServiceProvider.GetRequiredService<IProjectDatabase>().MigrateAsync(open.Folder, CancellationToken.None);
				if (!migration.Succeeded)
				{
					await catalog.CloseAsync(open.Id, CancellationToken.None);
					LogOpenProjectDatabaseFailed(app.Logger, open.Name, open.Folder, migration.Error);
				}
			}
			else
			{
				await catalog.CloseAsync(open.Id, CancellationToken.None);
				LogOpenProjectGone(app.Logger, open.Name, open.Folder);
			}
		}

		await catalog.RestoreOpenAsync(CancellationToken.None);
	}

	[LoggerMessage(Level = LogLevel.Warning, Message = "The open project {Name} was closed because its folder {Folder}, its database, or its hub is gone.")]
	private static partial void LogOpenProjectGone(ILogger logger, string name, string folder);

	[LoggerMessage(Level = LogLevel.Warning, Message = "The open project {Name} in {Folder} was closed because its database could not be brought up to date: {Reason}")]
	private static partial void LogOpenProjectDatabaseFailed(ILogger logger, string name, string folder, string? reason);
}
