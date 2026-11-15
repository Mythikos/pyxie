using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class RazorComponentParameterTests
{
	[Fact]
	public void EveryParameterWrittenInSourceMarkupIsDeclaredByItsComponent()
	{
		string root = RepositoryLayout.FindRoot();
		List<string> violations = [];

		foreach (string path in RepositoryLayout.EnumerateAuthoredFiles("src", "*.razor"))
		{
			foreach (RazorComponentUsage usage in RazorMarkupScanner.FindComponents(File.ReadAllText(path)))
			{
				foreach (string problem in RazorComponentCatalog.FindParameterProblems(usage))
				{
					violations.Add($"{Path.GetRelativePath(root, path)}:{usage.Line} {problem}");
				}
			}
		}

		Assert.True(violations.Count == 0, "Razor markup uses parameters that do not exist:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[Theory]
	[InlineData("<Check TValue=\"bool\" @bind-Checked=\"value\">Label</Check>", "Checked")]
	[InlineData("<Check TValue=\"bool\" Checked=\"@value\" CheckedChanged=\"@Changed\" />", "Checked")]
	[InlineData("<ModalContent Centered>", "Centered")]
	[InlineData("<Button Colour=\"Color.Primary\" />", "Colour")]
	public void FindParameterProblems_FlagsParametersTheComponentDoesNotDeclare(string markup, string parameter)
	{
		IReadOnlyList<string> problems = RazorMarkupScanner.FindComponents(markup).SelectMany(RazorComponentCatalog.FindParameterProblems).ToList();

		Assert.True(problems.Any(problem => problem.Contains($"'{parameter}'", StringComparison.Ordinal)), $"'{parameter}' should be flagged, got: {string.Join("; ", problems)}");
	}

	[Theory]
	[InlineData("<Check TValue=\"bool\" @bind-Value=\"value\">Label</Check>")]
	[InlineData("<Modal @bind-Visible=\"visible\" Centered><ModalContent><ModalBody /></ModalContent></Modal>")]
	[InlineData("<Select TValue=\"int?\" Value=\"@id\" ValueChanged=\"@Changed\" class=\"wide\" data-id=\"1\" />")]
	[InlineData("<Alert Color=\"Color.Danger\" Dismisable Visible=\"@(error is not null)\" VisibleChanged=\"@Changed\" />")]
	public void FindParameterProblems_AcceptsDeclaredParameters(string markup)
	{
		IReadOnlyList<string> problems = RazorMarkupScanner.FindComponents(markup).SelectMany(RazorComponentCatalog.FindParameterProblems).ToList();

		Assert.True(problems.Count == 0, string.Join("; ", problems));
	}

	[Fact]
	public void FindParameterProblems_IgnoresNamesThatAreNotComponents()
	{
		IReadOnlyList<string> problems = RazorMarkupScanner.FindComponents("@code { private List<Agent> _agents = []; }").SelectMany(RazorComponentCatalog.FindParameterProblems).ToList();

		Assert.Empty(problems);
	}
}
