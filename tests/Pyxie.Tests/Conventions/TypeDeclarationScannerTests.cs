using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class TypeDeclarationScannerTests
{
	[Fact]
	public void FindTypes_ReportsEveryKindIncludingNested()
	{
		const string source = """
			namespace Demo;

			public sealed class Outer
			{
				private enum Inner { A }
			}

			public interface IThing { }
			public readonly record struct Point(int X);
			internal record Person(string Name);
			""";

		IReadOnlyList<TypeDeclaration> types = TypeDeclarationScanner.FindTypes(source);

		Assert.Equal(["Outer", "Inner", "IThing", "Point", "Person"], types.Select(type => type.Name));
	}

	[Fact]
	public void FindTypes_IgnoresCommentsAndStrings()
	{
		const string source = """
			namespace Demo;

			// class Fake
			/* struct Fake2
			   enum Fake3 */
			public static class Real
			{
				public const string Text = "class NotAType";
				public static string Verbatim() => @"
			class AlsoNot";
			}
			""";

		IReadOnlyList<TypeDeclaration> types = TypeDeclarationScanner.FindTypes(source);

		Assert.Equal(["Real"], types.Select(type => type.Name));
	}

	[Fact]
	public void FindTypes_AppendsTypeParametersToExpectedFileStem()
	{
		IReadOnlyList<TypeDeclaration> types = TypeDeclarationScanner.FindTypes("public sealed record ServiceResult<T>(bool Succeeded);\npublic class Pair<out TKey, TValue> { }");

		Assert.Equal(["ServiceResultT", "PairTKeyTValue"], types.Select(type => type.ExpectedFileStem));
	}

	[Fact]
	public void FindNamespace_ReadsFileScopedNamespace()
	{
		Assert.Equal("Pyxie.Data.Entities", TypeDeclarationScanner.FindNamespace("using System;\n\nnamespace Pyxie.Data.Entities;\n"));
		Assert.Null(TypeDeclarationScanner.FindNamespace("var x = 1;"));
	}
}
