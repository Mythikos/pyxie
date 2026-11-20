using Pyxie.Data.Entities;
using Pyxie.Data.Sqlite.Persistence;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Adds closed projects to a rig's catalog without importing anything: with the files opening a project needs (its folder, its database, and an empty hub), or with no folder at all. Opening such a project works, and needs no git; it has no repository, so the Repository page does not show it as open.</summary>
internal static class StoredProjects
{
	/// <summary>Adds a closed project named after its folder under the rig's projects folder.</summary>
	/// <param name="rig">The rig whose catalog records the project.</param>
	/// <param name="name">The project's name, which is also its folder's name.</param>
	/// <param name="withFolder">Whether the folder is created with what opening the project needs; otherwise it does not exist.</param>
	/// <returns>The added project.</returns>
	public static async Task<Project> AddAsync(RepositoryRig rig, string name, bool withFolder = true)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		string folder = Path.Combine(rig.Layout.ProjectsFolder, name);

		if (withFolder)
		{
			ResultAssert.Succeeded(await new SqliteProjectDatabase().MigrateAsync(folder, token));
			Directory.CreateDirectory(WorkspaceLayout.HubPath(folder));
		}

		return ResultAssert.Succeeded(await rig.Catalog.AddAsync(name, folder, $"https://example.test/{name}.git", token));
	}
}
