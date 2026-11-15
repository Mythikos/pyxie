using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class NamespaceMirrorsFolderTests
{
	[Theory]
	[InlineData("src")]
	[InlineData("tests")]
	public void EveryTypeNamespaceIsTheProjectNamePlusItsFolderPath(string topLevelFolder)
	{
		string top = Path.Combine(RepositoryLayout.FindRoot(), topLevelFolder);
		List<string> violations = [];

		foreach (string path in RepositoryLayout.EnumerateAuthoredSources(topLevelFolder))
		{
			string source = File.ReadAllText(path);

			if (TypeDeclarationScanner.FindTypes(source).Count == 0)
			{
				continue;
			}

			string[] segments = Path.GetRelativePath(top, path).Split(Path.DirectorySeparatorChar);
			string expected = string.Join('.', segments[..^1]);
			string? actual = TypeDeclarationScanner.FindNamespace(source);

			if (!string.Equals(actual, expected, StringComparison.Ordinal))
			{
				violations.Add($"{Path.GetRelativePath(RepositoryLayout.FindRoot(), path)} declares namespace {actual ?? "(none)"}; expected {expected}");
			}
		}

		Assert.True(violations.Count == 0, "Namespaces mirror folders:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}
}
