using System.Text;
using System.Text.RegularExpressions;

namespace Pyxie.Tests.Support;

/// <summary>Finds type and namespace declarations in C# source text without a compiler dependency.</summary>
public static partial class TypeDeclarationScanner
{
	/// <summary>Finds every class, record, struct, enum, and interface declaration, including nested ones.</summary>
	/// <param name="source">The C# source text.</param>
	/// <returns>The declarations in source order.</returns>
	public static IReadOnlyList<TypeDeclaration> FindTypes(string source)
	{
		string code = StripNonCode(source);
		List<TypeDeclaration> declarations = [];

		foreach (Match match in TypePattern().Matches(code))
		{
			string name = match.Groups["name"].Value;
			string typeParameters = match.Groups["generic"].Success ? ParameterNames(match.Groups["generic"].Value) : string.Empty;
			declarations.Add(new TypeDeclaration(name, name + typeParameters));
		}

		return declarations;
	}

	/// <summary>Finds the namespace declared by the source, if any.</summary>
	/// <param name="source">The C# source text.</param>
	/// <returns>The namespace, or <see langword="null"/> when the source declares none.</returns>
	public static string? FindNamespace(string source)
	{
		Match match = NamespacePattern().Match(StripNonCode(source));
		return match.Success ? match.Groups["name"].Value : null;
	}

	private static string ParameterNames(string generic)
	{
		StringBuilder names = new();

		foreach (string parameter in generic.Trim('<', '>').Split(',', StringSplitOptions.TrimEntries))
		{
			names.Append(parameter.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]);
		}

		return names.ToString();
	}

	private static string StripNonCode(string source) => NonCodePattern().Replace(source, match => new string('\n', match.Value.Count(character => character == '\n')));

	[GeneratedRegex(@"""""""[\s\S]*?""""""|@""(?:[^""]|"""")*""|\$?""(?:\\.|[^""\\\r\n])*""|//[^\r\n]*|/\*[\s\S]*?\*/|'(?:\\.|[^'\\\r\n])'")]
	private static partial Regex NonCodePattern();

	[GeneratedRegex(@"^[ \t]*(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|unsafe|ref|new)\s+)*(?:record\s+(?:class|struct)|record|class|struct|enum|interface)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*(?<generic><[^>\r\n]*>)?", RegexOptions.Multiline)]
	private static partial Regex TypePattern();

	[GeneratedRegex(@"^[ \t]*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.Multiline)]
	private static partial Regex NamespacePattern();
}
