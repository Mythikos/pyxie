namespace Pyxie.Web.Components.Repository.Display;

/// <summary>One folder, or the file, on the way to the path the Files tab shows.</summary>
/// <param name="Name">The segment's own name.</param>
/// <param name="Path">The path from the repository root up to and including this segment.</param>
public sealed record BreadcrumbSegment(string Name, string Path);
