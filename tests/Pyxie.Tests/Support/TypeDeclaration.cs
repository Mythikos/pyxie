namespace Pyxie.Tests.Support;

/// <summary>A type declaration found in a C# source file.</summary>
/// <param name="Name">The declared name without type parameters.</param>
/// <param name="ExpectedFileStem">The file name, without extension, that the one-type-per-file rule requires.</param>
public sealed record TypeDeclaration(string Name, string ExpectedFileStem);
