namespace Pyxie.Tests.Support;

/// <summary>Locates the repository and enumerates its authored sources for convention tests.</summary>
public static class RepositoryLayout
{
	private const string SolutionFileName = "Pyxie.slnx";

	private static readonly string[] s_excludedDirectories = ["bin", "obj", "Migrations", ".vs", ".idea"];

	/// <summary>Gets the repository root, found by walking up from the test output to the solution file.</summary>
	/// <returns>The absolute root directory.</returns>
	public static string FindRoot()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
	}

	/// <summary>Enumerates authored C# files below a top-level folder, skipping build output and EF-generated migrations.</summary>
	/// <param name="topLevelFolder">A folder directly below the repository root, such as <c>src</c>.</param>
	/// <returns>Absolute file paths.</returns>
	public static IReadOnlyList<string> EnumerateAuthoredSources(string topLevelFolder) => EnumerateAuthoredFiles(topLevelFolder, "*.cs");

	/// <summary>Enumerates authored files matching a pattern below a top-level folder, skipping build output and EF-generated migrations.</summary>
	/// <param name="topLevelFolder">A folder directly below the repository root, such as <c>src</c>.</param>
	/// <param name="searchPattern">The file name pattern, such as <c>*.razor</c>.</param>
	/// <returns>Absolute file paths.</returns>
	public static IReadOnlyList<string> EnumerateAuthoredFiles(string topLevelFolder, string searchPattern)
	{
		string root = FindRoot();
		string top = Path.Combine(root, topLevelFolder);

		return Directory.EnumerateFiles(top, searchPattern, SearchOption.AllDirectories).Where(path => !Path.GetRelativePath(top, path).Split(Path.DirectorySeparatorChar).Any(segment => s_excludedDirectories.Contains(segment))).OrderBy(path => path, StringComparer.Ordinal).ToList();
	}
}
