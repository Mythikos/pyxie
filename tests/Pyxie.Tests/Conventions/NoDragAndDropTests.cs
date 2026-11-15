using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class NoDragAndDropTests
{
	private static readonly string[] s_dragAndDropComponents = ["DropContainer", "DropZone"];

	[Fact]
	public void SourceMarkupUsesNoDragAndDropComponents()
	{
		string root = RepositoryLayout.FindRoot();
		List<string> violations = [];

		foreach (string path in RepositoryLayout.EnumerateAuthoredFiles("src", "*.razor"))
		{
			foreach (string component in FindDragAndDropComponents(File.ReadAllText(path)))
			{
				violations.Add($"{Path.GetRelativePath(root, path)} uses <{component}>");
			}
		}

		Assert.True(violations.Count == 0, "Agents advance cards between lanes, so people must not drag them (see AGENTS.md):" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[Theory]
	[InlineData("<DropContainer TItem=\"WorkCard\" Items=\"@cards\"><ChildContent /></DropContainer>", "DropContainer")]
	[InlineData("<div><DropZone TItem=\"WorkCard\" Name=\"Queue\" /></div>", "DropZone")]
	public void FindDragAndDropComponents_FlagsBlazoriseDragAndDrop(string markup, string component)
	{
		Assert.Equal([component], FindDragAndDropComponents(markup));
	}

	[Fact]
	public void FindDragAndDropComponents_IgnoresOtherComponentsAndComments()
	{
		const string markup = """
			@* <DropZone TItem="WorkCard" /> *@
			<Button Color="Color.Primary">Add</Button>
			""";

		Assert.Empty(FindDragAndDropComponents(markup));
	}

	private static IEnumerable<string> FindDragAndDropComponents(string markup)
	{
		return RazorMarkupScanner.FindComponents(markup).Select(usage => usage.Name).Where(name => s_dragAndDropComponents.Contains(name));
	}
}
