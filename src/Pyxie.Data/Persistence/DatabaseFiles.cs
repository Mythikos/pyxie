namespace Pyxie.Data.Persistence;

/// <summary>Owns the name of Pyxie's database files: the machine database in the workspace root and each project's database in its folder have the same name.</summary>
public static class DatabaseFiles
{
	/// <summary>The file name of a Pyxie database.</summary>
	public const string FileName = "pyxie.db";

	/// <summary>Gets the database file in a folder.</summary>
	/// <param name="folder">The workspace root, for the machine database, or a project's folder.</param>
	/// <returns>The full path of the database file.</returns>
	public static string PathIn(string folder)
	{
		return Path.Combine(folder, FileName);
	}
}
