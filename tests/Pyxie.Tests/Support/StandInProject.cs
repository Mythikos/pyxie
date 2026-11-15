using Pyxie.Data.Entities;
using Pyxie.Data.Projects;

namespace Pyxie.Tests.Support;

/// <summary>An open project for component tests whose services already point at a test database: the pages show a project's board, gates, and settings only while one is open, and the folder is never read, because the test's own context factory stands in for the project's database.</summary>
public static class StandInProject
{
	/// <summary>Creates an open project holder with a stand-in project open.</summary>
	/// <returns>The holder to register as the application's <see cref="OpenProject"/>.</returns>
	public static OpenProject Open()
	{
		OpenProject openProject = new();
		openProject.Set(new Project { Id = 1, Name = "stand-in", Folder = Path.Combine(Path.GetTempPath(), "pyxie-stand-in-project"), IsOpen = true });

		return openProject;
	}
}
