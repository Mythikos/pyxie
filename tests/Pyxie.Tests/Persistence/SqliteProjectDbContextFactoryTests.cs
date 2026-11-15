using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Tests.Runtime.Git;

using Xunit;

namespace Pyxie.Tests.Persistence;

/// <summary>The project context factory works on whichever project is open: it refuses while none is, and every context it creates is on the open project's own database file.</summary>
public sealed class SqliteProjectDbContextFactoryTests
{
	private const string FirstOnlyCategory = "Only in first";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void CreateDbContext_WithNoProjectOpen_IsRefused()
	{
		SqliteProjectDbContextFactory factory = new(new OpenProject());

		InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());

		Assert.Equal("No project is open.", refused.Message);
	}

	[Fact]
	public async Task CreateDbContext_UsesTheDatabaseInTheOpenProjectsFolderAndFollowsTheOpenProject()
	{
		await using ScratchFolder projects = ScratchFolder.Create("pyxie-projects");
		string first = projects.Combine("first");
		string second = projects.Combine("second");
		SqliteProjectDatabase projectDatabase = new();
		await projectDatabase.MigrateAsync(first, Token);
		await projectDatabase.MigrateAsync(second, Token);
		OpenProject openProject = new();
		IDbContextFactory<ProjectDbContext> factory = new SqliteProjectDbContextFactory(openProject);

		openProject.Set(ProjectIn(1, first));
		await using (ProjectDbContext context = await factory.CreateDbContextAsync(Token))
		{
			Assert.Equal(DatabaseFiles.PathIn(first), context.Database.GetDbConnection().DataSource, ignoreCase: true);
			context.Categories.Add(new Category { Name = FirstOnlyCategory });
			await context.SaveChangesAsync(Token);
		}

		openProject.Set(ProjectIn(2, second));
		await using (ProjectDbContext context = await factory.CreateDbContextAsync(Token))
		{
			Assert.Equal(DatabaseFiles.PathIn(second), context.Database.GetDbConnection().DataSource, ignoreCase: true);
			Assert.DoesNotContain(FirstOnlyCategory, await CategoryNamesAsync(context));
		}

		openProject.Set(ProjectIn(1, first));
		await using (ProjectDbContext context = await factory.CreateDbContextAsync(Token))
		{
			Assert.Contains(FirstOnlyCategory, await CategoryNamesAsync(context));
		}

		openProject.Set(null);
		Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());
	}

	private static Project ProjectIn(int id, string folder)
	{
		return new Project { Id = id, Name = Path.GetFileName(folder), Folder = folder, IsOpen = true };
	}

	private static Task<List<string>> CategoryNamesAsync(ProjectDbContext context)
	{
		return context.Categories.Select(category => category.Name).ToListAsync(Token);
	}
}
