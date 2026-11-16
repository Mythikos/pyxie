using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerImages.UnusedBaseImages"/>: which of Pyxie's base images nothing needs. The current base image is always kept; any other is kept only while some project image is built on it, which is the case when the project image's layers start with all of the base's layers. Layers are written as comma-separated names here.</summary>
public sealed class DockerImagesUnusedBaseImagesTests
{
	private const string Current = "current";
	private const string Old = "old";

	private static IReadOnlyList<string> Layers(string layers)
	{
		return layers.Split(',');
	}

	private static Dictionary<string, IReadOnlyList<string>> Bases(params (string Id, string Layers)[] bases)
	{
		return bases.ToDictionary(entry => entry.Id, entry => Layers(entry.Layers), StringComparer.Ordinal);
	}

	private static IReadOnlyList<string> Unused(Dictionary<string, IReadOnlyList<string>> bases, string? currentBaseId, params string[] projects)
	{
		return DockerImages.UnusedBaseImages(bases, currentBaseId, [.. projects.Select(Layers)]);
	}

	[Theory]
	[InlineData("a,b", "a,b,c", false)]
	[InlineData("a,b", "a,b", false)]
	[InlineData("a", "a,b,c", false)]
	[InlineData("a,b", "c,d", true)]
	[InlineData("b", "a,b", true)]
	[InlineData("a,b,c", "a,b", true)]
	[InlineData("a,x", "a,b,c", true)]
	[InlineData("a,b,c", "a,b,d", true)]
	public void UnusedBaseImages_AnOldBaseAndOneProjectImage_KeepsTheBaseOnlyWhenTheProjectsLayersStartWithItsLayers(string oldBaseLayers, string projectLayers, bool removed)
	{
		string[] expected = removed ? [Old] : [];

		IReadOnlyList<string> unused = Unused(Bases((Current, "z"), (Old, oldBaseLayers)), Current, projectLayers);

		Assert.Equal(expected, unused);
	}

	[Fact]
	public void UnusedBaseImages_TheCurrentBaseWithNoProjectOnIt_IsKept()
	{
		IReadOnlyList<string> unused = Unused(Bases((Current, "a,b"), (Old, "a,c")), Current);

		Assert.Equal([Old], unused);
	}

	[Fact]
	public void UnusedBaseImages_TheCurrentBaseWhileProjectsAreOnAnotherBase_IsKeptAndTheOtherBaseToo()
	{
		IReadOnlyList<string> unused = Unused(Bases((Current, "n,e,w"), (Old, "o,l,d")), Current, "o,l,d,tools");

		Assert.Empty(unused);
	}

	[Fact]
	public void UnusedBaseImages_ABaseOnlyOneOfSeveralProjectsIsBuiltOn_IsKept()
	{
		IReadOnlyList<string> unused = Unused(Bases((Current, "z"), (Old, "a,b")), Current, "x,y,tools", "a,b,tools", "q");

		Assert.Empty(unused);
	}

	[Fact]
	public void UnusedBaseImages_NoBaseImages_IsEmpty()
	{
		Assert.Empty(Unused(Bases(), Current, "a,b"));
	}

	[Fact]
	public void UnusedBaseImages_NoCurrentBaseBuilt_RemovesEveryBaseNoProjectIsBuiltOn()
	{
		IReadOnlyList<string> unused = Unused(Bases(("one", "a,b"), ("two", "c,d"), ("three", "e,f")), currentBaseId: null, "c,d,tools");

		Assert.Equal(["one", "three"], unused);
	}

	[Fact]
	public void UnusedBaseImages_NoCurrentBaseBuiltAndNoProjects_RemovesEveryBase()
	{
		IReadOnlyList<string> unused = Unused(Bases(("one", "a,b"), ("two", "c,d")), currentBaseId: null);

		Assert.Equal(["one", "two"], unused);
	}

	[Fact]
	public void UnusedBaseImages_SeveralUnusedBases_AreOrderedByOrdinalComparisonWhateverOrderTheyWereGivenIn()
	{
		IReadOnlyList<string> unused = Unused(Bases(("b", "a"), ("a", "b"), ("B", "c"), ("A", "d"), (Current, "z")), Current);

		Assert.Equal(["A", "B", "a", "b"], unused);
	}
}
