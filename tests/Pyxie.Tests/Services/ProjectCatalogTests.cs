using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>The project catalog keeps names and folders unique ignoring case, at most one project open, and the open project in memory in step with the machine database.</summary>
public sealed class ProjectCatalogTests
{
	private const string Source = "https://example.test/owner/app.git";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string FolderFor(string name) => Path.Combine(Path.GetTempPath(), "pyxie-test-projects", name);

	[Fact]
	public async Task AddAsync_StoresAClosedProjectWithItsTrimmedNameFullFolderAndTheTime()
	{
		await using MachineTestDatabase database = new();
		DateTime addedAt = database.Clock.Advance(TimeSpan.FromHours(2));

		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("  app  ", $" {FolderFor("app")}{Path.DirectorySeparatorChar} ", Source, Token));

		Project stored = Assert.Single(await database.Catalog.ListAsync(Token));
		Assert.Equal((added.Id, "app", FolderFor("app"), addedAt, false), (stored.Id, stored.Name, stored.Folder, stored.CreatedUtc, stored.IsOpen));
		Assert.Null(database.OpenProject.Current);
	}

	[Fact]
	public async Task AddAsync_StoresTheTrimmedSourceAndReturnsItOnTheAddedProject()
	{
		await using MachineTestDatabase database = new();

		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), $"  {Source}  ", Token));

		Assert.Equal(Source, added.Source);
		Assert.Equal(Source, Assert.Single(await database.Catalog.ListAsync(Token)).Source);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public async Task AddAsync_RefusesABlankSourceAndAddsNothing(string source)
	{
		await using MachineTestDatabase database = new();

		string error = ResultAssert.Failed(await database.Catalog.AddAsync("app", FolderFor("app"), source, Token));

		Assert.Equal("Source is required.", error);
		Assert.Empty(await database.Catalog.ListAsync(Token));
	}

	[Fact]
	public async Task AddAsync_RefusesASourceLongerThanTheLimitAndStoresOneAtTheLimit()
	{
		await using MachineTestDatabase database = new();

		string error = ResultAssert.Failed(await database.Catalog.AddAsync("long", FolderFor("long"), new string('s', PyxieLimits.MaxOriginLength + 1), Token));
		ResultAssert.Succeeded(await database.Catalog.AddAsync("exact", FolderFor("exact"), new string('s', PyxieLimits.MaxOriginLength), Token));

		Assert.Equal($"Source must be {PyxieLimits.MaxOriginLength} characters or fewer.", error);
		Assert.Equal(["exact"], (await database.Catalog.ListAsync(Token)).Select(project => project.Name));
	}

	[Theory]
	[InlineData("", "app", "Project name is required.")]
	[InlineData("app", "", "Folder is required.")]
	[InlineData("app", "relative/app", "Folder must be a full path.")]
	public async Task AddAsync_RejectsInvalidFields(string name, string folder, string expected)
	{
		await using MachineTestDatabase database = new();

		string error = ResultAssert.Failed(await database.Catalog.AddAsync(name, folder, Source, Token));

		Assert.Equal(expected, error);
		Assert.Empty(await database.Catalog.ListAsync(Token));
	}

	[Fact]
	public async Task AddAsync_RefusesANameOrAFolderThatIsTakenIgnoringCase()
	{
		await using MachineTestDatabase database = new();
		ResultAssert.Succeeded(await database.Catalog.AddAsync("App", FolderFor("app"), Source, Token));

		Assert.Equal("A project named 'APP' already exists.", ResultAssert.Failed(await database.Catalog.AddAsync("APP", FolderFor("other"), Source, Token)));
		Assert.Equal(
			$"The folder {FolderFor("APP")} already belongs to the project 'App'.",
			ResultAssert.Failed(await database.Catalog.AddAsync("Other", FolderFor("APP"), Source, Token)));
		Assert.Single(await database.Catalog.ListAsync(Token));
	}

	[Fact]
	public async Task OpenAsync_OpensTheProjectAndMakesItTheOpenProject()
	{
		await using MachineTestDatabase database = new();
		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));

		Project opened = ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));

		Assert.True(opened.IsOpen, "The returned project is open.");
		Assert.Equal((added.Id, FolderFor("app")), (database.OpenProject.Current?.Id, database.OpenProject.Current?.Folder));
		Assert.Equal(added.Id, (await database.Catalog.GetOpenAsync(Token))?.Id);
	}

	[Fact]
	public async Task OpenAsync_WhileAnotherProjectIsOpen_ClosesItAndOpensTheProject()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		Project second = ResultAssert.Succeeded(await database.Catalog.AddAsync("second", FolderFor("second"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(first.Id, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		Project opened = ResultAssert.Succeeded(await database.Catalog.OpenAsync(second.Id, Token));

		Assert.True(opened.IsOpen, "The returned project is open.");
		Assert.Equal(second.Id, database.OpenProject.Current?.Id);
		Assert.Equal(second.Id, (await database.Catalog.GetOpenAsync(Token))?.Id);
		Assert.Equal([(first.Id, false), (second.Id, true)], (await database.Catalog.ListAsync(Token)).Select(project => (project.Id, project.IsOpen)));
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task OpenAsync_OfTheOpenProject_SucceedsAndLeavesItTheOnlyOpenProject()
	{
		await using MachineTestDatabase database = new();
		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));
		Project other = ResultAssert.Succeeded(await database.Catalog.AddAsync("other", FolderFor("other"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));

		ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));

		Assert.Equal(added.Id, database.OpenProject.Current?.Id);
		Assert.Equal([(added.Id, true), (other.Id, false)], (await database.Catalog.ListAsync(Token)).Select(project => (project.Id, project.IsOpen)));
	}

	[Fact]
	public async Task OpenAsync_OfAProjectThatDoesNotExist_SaysSo()
	{
		await using MachineTestDatabase database = new();

		Assert.Equal("Project not found.", ResultAssert.Failed(await database.Catalog.OpenAsync(42, Token)));
		Assert.Null(database.OpenProject.Current);
	}

	[Fact]
	public async Task OpenAsync_OfAProjectThatDoesNotExistWhileAnotherIsOpen_SaysSoAndLeavesTheOpenOneOpen()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(first.Id, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		string error = ResultAssert.Failed(await database.Catalog.OpenAsync(42, Token));

		Assert.Equal("Project not found.", error);
		Assert.Equal(first.Id, database.OpenProject.Current?.Id);
		Assert.Equal(first.Id, (await database.Catalog.GetOpenAsync(Token))?.Id);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task CloseAsync_ClosesTheOpenProjectAndClearsTheOpenProject()
	{
		await using MachineTestDatabase database = new();
		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		ResultAssert.Succeeded(await database.Catalog.CloseAsync(added.Id, Token));

		Assert.Null(database.OpenProject.Current);
		Assert.Null(await database.Catalog.GetOpenAsync(Token));
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task CloseAsync_OfTheOpenProject_LeavesTheOtherProjectsClosed()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		Project second = ResultAssert.Succeeded(await database.Catalog.AddAsync("second", FolderFor("second"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(second.Id, Token));

		ResultAssert.Succeeded(await database.Catalog.CloseAsync(second.Id, Token));

		Assert.Equal([(first.Id, false), (second.Id, false)], (await database.Catalog.ListAsync(Token)).Select(project => (project.Id, project.IsOpen)));
	}

	[Fact]
	public async Task CloseAsync_OfAProjectThatIsNotOpenWhileAnotherIsOpen_SaysSoAndLeavesTheOpenOneOpen()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		Project second = ResultAssert.Succeeded(await database.Catalog.AddAsync("second", FolderFor("second"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(second.Id, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		string error = ResultAssert.Failed(await database.Catalog.CloseAsync(first.Id, Token));

		Assert.Equal("The project is not open.", error);
		Assert.Equal(second.Id, database.OpenProject.Current?.Id);
		Assert.Equal(second.Id, (await database.Catalog.GetOpenAsync(Token))?.Id);
		Assert.Equal([(first.Id, false), (second.Id, true)], (await database.Catalog.ListAsync(Token)).Select(project => (project.Id, project.IsOpen)));
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task CloseAsync_OfAProjectThatDoesNotExistWhileAnotherIsOpen_SaysSoAndLeavesTheOpenOneOpen()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(first.Id, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		string error = ResultAssert.Failed(await database.Catalog.CloseAsync(42, Token));

		Assert.Equal("Project not found.", error);
		Assert.Equal(first.Id, database.OpenProject.Current?.Id);
		Assert.Equal(first.Id, (await database.Catalog.GetOpenAsync(Token))?.Id);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task CloseAsync_WhenNoProjectIsOpen_IsRefusedWhetherOrNotTheProjectExists()
	{
		await using MachineTestDatabase database = new();
		Project closed = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		Assert.Equal("The project is not open.", ResultAssert.Failed(await database.Catalog.CloseAsync(closed.Id, Token)));
		Assert.Equal("Project not found.", ResultAssert.Failed(await database.Catalog.CloseAsync(42, Token)));

		Assert.Null(database.OpenProject.Current);
		Assert.Equal(0, changes);
	}

	[Fact]
	public async Task RemoveAsync_RemovesAClosedProjectButRefusesTheOpenOne()
	{
		await using MachineTestDatabase database = new();
		Project closed = ResultAssert.Succeeded(await database.Catalog.AddAsync("closed", FolderFor("closed"), Source, Token));
		Project open = ResultAssert.Succeeded(await database.Catalog.AddAsync("open", FolderFor("open"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(open.Id, Token));

		ResultAssert.Succeeded(await database.Catalog.RemoveAsync(closed.Id, Token));
		string error = ResultAssert.Failed(await database.Catalog.RemoveAsync(open.Id, Token));

		Assert.Equal("Close the project before removing it.", error);
		Assert.Equal([open.Id], (await database.Catalog.ListAsync(Token)).Select(project => project.Id));
		Assert.Equal("Project not found.", ResultAssert.Failed(await database.Catalog.RemoveAsync(closed.Id, Token)));
	}

	[Fact]
	public async Task RefusedOpenCloseAndRemove_LeaveTheOpenProjectAsItWasAndAnnounceNothing()
	{
		await using MachineTestDatabase database = new();
		Project first = ResultAssert.Succeeded(await database.Catalog.AddAsync("first", FolderFor("first"), Source, Token));
		int changes = 0;
		database.OpenProject.Changed += () => changes++;

		ResultAssert.Failed(await database.Catalog.CloseAsync(first.Id, Token));
		ResultAssert.Failed(await database.Catalog.OpenAsync(42, Token));

		Assert.Null(database.OpenProject.Current);
		Assert.Equal(0, changes);

		ResultAssert.Succeeded(await database.Catalog.OpenAsync(first.Id, Token));
		ResultAssert.Failed(await database.Catalog.OpenAsync(42, Token));
		ResultAssert.Failed(await database.Catalog.RemoveAsync(first.Id, Token));

		Assert.Equal(first.Id, database.OpenProject.Current?.Id);
		Assert.Equal(1, changes);
	}

	[Fact]
	public async Task OpenAndClose_ChangeTheOpenProjectOnlyOnceTheirWriteIsCommitted()
	{
		// A database file, so the handler reads on a connection of its own and sees only what was committed.
		await using ScratchFolder root = ScratchFolder.Create("pyxie-machine");
		ServiceCollection services = new();
		services.AddDbContextFactory<MachineDbContext>(options => PyxieSqlite.ConfigureMachine(options, PyxieSqlite.ConnectionString(DatabaseFiles.PathIn(root.Folder))));
		await using ServiceProvider provider = services.BuildServiceProvider();
		IDbContextFactory<MachineDbContext> contextFactory = provider.GetRequiredService<IDbContextFactory<MachineDbContext>>();
		await using (MachineDbContext context = await contextFactory.CreateDbContextAsync(Token))
		{
			await context.Database.MigrateAsync(Token);
		}

		OpenProject openProject = new();
		ProjectCatalog catalog = new(contextFactory, openProject, new TestClock());
		Project added = ResultAssert.Succeeded(await catalog.AddAsync("app", FolderFor("app"), Source, Token));
		Project other = ResultAssert.Succeeded(await catalog.AddAsync("other", FolderFor("other"), Source, Token));
		List<(int? Current, int? Committed)> seen = [];
		openProject.Changed += () =>
		{
			using MachineDbContext reader = contextFactory.CreateDbContext();
			seen.Add((openProject.Current?.Id, reader.Projects.AsNoTracking().Where(project => project.IsOpen).Select(project => (int?)project.Id).SingleOrDefault()));
		};

		ResultAssert.Succeeded(await catalog.OpenAsync(added.Id, Token));
		ResultAssert.Succeeded(await catalog.OpenAsync(other.Id, Token));
		ResultAssert.Succeeded(await catalog.CloseAsync(other.Id, Token));

		Assert.Equal([(added.Id, added.Id), (other.Id, other.Id), (null, null)], seen);
	}

	[Fact]
	public async Task OpenProject_IsASnapshotThatChangingTheReturnedProjectDoesNotTouch()
	{
		await using MachineTestDatabase database = new();
		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));

		Project opened = ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));
		opened.Name = "renamed";
		opened.Folder = FolderFor("elsewhere");

		Assert.Equal(("app", FolderFor("app")), (database.OpenProject.Current?.Name, database.OpenProject.Current?.Folder));
	}

	[Fact]
	public async Task RestoreOpenAsync_MakesTheOpenProjectMatchTheDatabase()
	{
		await using MachineTestDatabase database = new();
		Project added = ResultAssert.Succeeded(await database.Catalog.AddAsync("app", FolderFor("app"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.OpenAsync(added.Id, Token));
		OpenProject afterRestart = new();
		ProjectCatalog restarted = new(database.ContextFactory, afterRestart, database.Clock);

		Project? restored = await restarted.RestoreOpenAsync(Token);

		Assert.Equal(added.Id, restored?.Id);
		Assert.Equal(added.Id, afterRestart.Current?.Id);
	}

	[Fact]
	public async Task ListAsync_OrdersProjectsByName()
	{
		await using MachineTestDatabase database = new();
		ResultAssert.Succeeded(await database.Catalog.AddAsync("beta", FolderFor("beta"), Source, Token));
		ResultAssert.Succeeded(await database.Catalog.AddAsync("alpha", FolderFor("alpha"), Source, Token));

		Assert.Equal(["alpha", "beta"], (await database.Catalog.ListAsync(Token)).Select(project => project.Name));
	}
}
