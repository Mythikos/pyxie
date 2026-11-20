using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Sqlite.DependencyInjection;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Projects;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Runs the application's database startup against scratch roots, including a project recorded as open by a previous process. The internal hosting extension is reached by name, as in the endpoint tests.</summary>
public sealed class PyxieWebApplicationExtensionsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task OpenPyxieDatabasesAsync_WhenTheOpenDatabaseCannotBeMigrated_ClosesItLogsWhyAndPreservesItsFiles(bool fromAnotherBuild)
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		Project project = await StoreOpenProjectAsync(scratch.Folder);
		await ProjectDatabaseFile.MakeUnopenableAsync(project.Folder, fromAnotherBuild, Token);
		byte[] before = await File.ReadAllBytesAsync(DatabaseFiles.PathIn(project.Folder), Token);
		using CapturingLoggerProvider logs = new();
		await using WebApplication app = CreateApp(scratch.Folder, logs);

		await OpenDatabasesAsync(app);

		Assert.Null(app.Services.GetRequiredService<OpenProject>().Current);
		await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
		IProjectCatalog catalog = scope.ServiceProvider.GetRequiredService<IProjectCatalog>();
		Assert.Null(await catalog.GetOpenAsync(Token));
		Project closed = Assert.Single(await catalog.ListAsync(Token));
		Assert.Equal(project.Id, closed.Id);
		Assert.False(closed.IsOpen, "Startup keeps the project in the catalog, closed.");
		Assert.Equal(before, await File.ReadAllBytesAsync(DatabaseFiles.PathIn(project.Folder), Token));
		Assert.True(Directory.Exists(WorkspaceLayout.HubPath(project.Folder)), "Startup never deletes the project's hub.");
		CapturedLogEntry warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
		Assert.Contains(project.Name, warning.Message, StringComparison.Ordinal);
		Assert.Contains("database", warning.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("import", warning.Message, StringComparison.OrdinalIgnoreCase);

		// A second launch sees the persisted close and needs no recovery.
		await OpenDatabasesAsync(app);
		Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task OpenPyxieDatabasesAsync_WithAUsableProject_RestoresItAndAppliesPendingMigrations(bool emptyDatabase)
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		Project project = await StoreOpenProjectAsync(scratch.Folder);
		if (emptyDatabase)
		{
			await File.WriteAllBytesAsync(DatabaseFiles.PathIn(project.Folder), [], Token);
		}

		using CapturingLoggerProvider logs = new();
		await using WebApplication app = CreateApp(scratch.Folder, logs);

		await OpenDatabasesAsync(app);

		Assert.Equal(project.Id, app.Services.GetRequiredService<OpenProject>().Current?.Id);
		await using ProjectDbContext context = ProjectDatabaseFile.Open(project.Folder);
		Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync(Token));
		Assert.Empty(await context.Database.GetPendingMigrationsAsync(Token));
		Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
	}

	[Theory]
	[InlineData("pyxie.db")]
	[InlineData("hub")]
	public async Task OpenPyxieDatabasesAsync_WhenTheOpenProjectIsMissingFiles_ClosesItAndLogsWhy(string missing)
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		Project project = await StoreOpenProjectAsync(scratch.Folder);
		string path = Path.Combine(project.Folder, missing);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
		else
		{
			Directory.Delete(path);
		}

		using CapturingLoggerProvider logs = new();
		await using WebApplication app = CreateApp(scratch.Folder, logs);

		await OpenDatabasesAsync(app);

		Assert.Null(app.Services.GetRequiredService<OpenProject>().Current);
		await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
		Assert.Null(await scope.ServiceProvider.GetRequiredService<IProjectCatalog>().GetOpenAsync(Token));
		CapturedLogEntry warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
		Assert.Contains("gone", warning.Message, StringComparison.Ordinal);
		Assert.Contains(project.Name, warning.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task OpenPyxieDatabasesAsync_WithANewRoot_CreatesTheMachineDatabaseWithNoOpenProject()
	{
		await using ScratchFolder scratch = ScratchFolder.Create();
		string root = scratch.Combine("new-root");
		using CapturingLoggerProvider logs = new();
		await using WebApplication app = CreateApp(root, logs);

		await OpenDatabasesAsync(app);

		Assert.True(File.Exists(DatabaseFiles.PathIn(root)), "Startup creates the root and machine database.");
		Assert.Null(app.Services.GetRequiredService<OpenProject>().Current);
		await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
		Assert.Empty(await scope.ServiceProvider.GetRequiredService<IProjectCatalog>().ListAsync(Token));
		Assert.DoesNotContain(logs.Entries, entry => entry.Level >= LogLevel.Warning);
	}

	private static async Task<Project> StoreOpenProjectAsync(string root)
	{
		await using RepositoryRig rig = new(root);
		Project project = await StoredProjects.AddAsync(rig, "app");

		return ResultAssert.Succeeded(await rig.Catalog.OpenAsync(project.Id, Token));
	}

	private static WebApplication CreateApp(string root, CapturingLoggerProvider logs)
	{
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.Logging.AddProvider(logs);
		builder.Services.AddSingleton(new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = root })));
		builder.Services.AddPyxieSqlite(provider => provider.GetRequiredService<WorkspaceLayout>().MachineDatabasePath);
		builder.Services.AddPyxieData();

		return builder.Build();
	}

	private static Task OpenDatabasesAsync(WebApplication app)
	{
		Type extensions = typeof(ProjectList).Assembly.GetType("Pyxie.Web.Hosting.PyxieWebApplicationExtensions") ?? throw new InvalidOperationException("Pyxie.Web no longer has PyxieWebApplicationExtensions.");
		MethodInfo open = extensions.GetMethod("OpenPyxieDatabasesAsync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(extensions.Name, "OpenPyxieDatabasesAsync");

		return (Task)(open.Invoke(null, [app]) ?? throw new InvalidOperationException("Database startup did not return a task."));
	}
}
