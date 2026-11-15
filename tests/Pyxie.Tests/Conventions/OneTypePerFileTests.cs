using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class OneTypePerFileTests
{
	private const string RazorCodeBehindSuffix = ".razor";

	[Theory]
	[InlineData("src")]
	[InlineData("tests")]
	public void EveryFileDeclaresAtMostOneTypeNamedAfterTheFile(string topLevelFolder)
	{
		List<string> violations = [];

		foreach (string path in RepositoryLayout.EnumerateAuthoredSources(topLevelFolder))
		{
			IReadOnlyList<TypeDeclaration> types = TypeDeclarationScanner.FindTypes(File.ReadAllText(path));
			string relative = Path.GetRelativePath(RepositoryLayout.FindRoot(), path);

			if (types.Count == 0)
			{
				continue;
			}

			if (types.Count > 1)
			{
				violations.Add($"{relative} declares {types.Count} types: {string.Join(", ", types.Select(type => type.Name))}");
				continue;
			}

			string stem = Path.GetFileNameWithoutExtension(path);
			stem = stem.EndsWith(RazorCodeBehindSuffix, StringComparison.Ordinal) ? stem[..^RazorCodeBehindSuffix.Length] : stem;

			if (!string.Equals(stem, types[0].ExpectedFileStem, StringComparison.Ordinal))
			{
				violations.Add($"{relative} declares {types[0].Name}; expected the file to be named {types[0].ExpectedFileStem}.cs");
			}
		}

		Assert.True(violations.Count == 0, "One type per file, named after the type:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}
}
