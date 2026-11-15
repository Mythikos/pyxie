namespace Pyxie.Tests.Support;

/// <summary>A component element found in Razor markup.</summary>
/// <param name="Name">The element name, such as <c>Check</c>.</param>
/// <param name="Line">The one-based line of the opening tag.</param>
/// <param name="Attributes">The attribute names exactly as written, such as <c>@bind-Value</c>.</param>
public sealed record RazorComponentUsage(string Name, int Line, IReadOnlyList<string> Attributes);
