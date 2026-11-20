namespace Pyxie.Data.Services.Internal;

/// <summary>Owns how messages list several names: "a and b" for two, and "a, b, and c" for more.</summary>
internal static class EnglishLists
{
	/// <summary>Joins names into an English list ending with "and".</summary>
	/// <param name="names">The names, in the order they are listed.</param>
	/// <returns>The list, such as <c>a, b, and c</c>; the name alone for one, and empty for none.</returns>
	public static string JoinWithAnd(IReadOnlyList<string> names)
	{
		return names.Count switch
		{
			0 => string.Empty,
			1 => names[0],
			2 => $"{names[0]} and {names[1]}",
			_ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
		};
	}
}
