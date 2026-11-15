namespace Pyxie.Web.Components.Repository.Display;

/// <summary>Splits a path into the links of a breadcrumb.</summary>
public static class BreadcrumbTrail
{
	/// <summary>Lists the segments of a path, each with the path that reaches it.</summary>
	/// <param name="path">A path from the repository root with forward slashes.</param>
	/// <returns>The segments from the top down; empty for the repository root.</returns>
	public static IReadOnlyList<BreadcrumbSegment> Of(string path)
	{
		List<BreadcrumbSegment> segments = [];
		string reached = string.Empty;

		foreach (string name in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			reached = reached.Length == 0 ? name : $"{reached}/{name}";
			segments.Add(new BreadcrumbSegment(name, reached));
		}

		return segments;
	}
}
