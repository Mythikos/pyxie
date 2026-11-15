using System.Text.RegularExpressions;

using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class BoardLaneEnumerationTests
{
	private static readonly Regex s_laneEnumeration = new(@"Enum\s*\.\s*Get(Values|Names|ValuesAsUnderlyingType)\s*(<\s*CardLane\s*>|\(\s*typeof\s*\(\s*CardLane\s*\)\s*\))", RegexOptions.CultureInvariant);

	[Fact]
	public void SourceNeverEnumeratesEveryCardLaneToDecideWhatTheBoardShows()
	{
		string root = RepositoryLayout.FindRoot();
		List<string> violations = [];

		foreach (string path in RepositoryLayout.EnumerateAuthoredSources("src").Concat(RepositoryLayout.EnumerateAuthoredFiles("src", "*.razor")))
		{
			if (EnumeratesEveryLane(File.ReadAllText(path)))
			{
				violations.Add(Path.GetRelativePath(root, path));
			}
		}

		Assert.True(violations.Count == 0, "Closed cards leave the board, so use CardLifecycleRules.BoardLanes instead of every CardLane value in:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
	}

	[Theory]
	[InlineData("foreach (CardLane lane in Enum.GetValues<CardLane>())")]
	[InlineData("Enum.GetValues(typeof(CardLane))")]
	[InlineData("Enum . GetNames < CardLane > ()")]
	public void EnumeratesEveryLane_FlagsEnumerationOverTheLaneEnum(string source)
	{
		Assert.True(EnumeratesEveryLane(source), $"'{source}' should be flagged.");
	}

	[Theory]
	[InlineData("foreach (CardLane lane in CardLifecycleRules.BoardLanes)")]
	[InlineData("Enum.GetValues<HarnessKind>()")]
	[InlineData("Enum.IsDefined(lane)")]
	public void EnumeratesEveryLane_IgnoresEverythingElse(string source)
	{
		Assert.False(EnumeratesEveryLane(source), $"'{source}' should not be flagged.");
	}

	private static bool EnumeratesEveryLane(string source)
	{
		return s_laneEnumeration.IsMatch(source);
	}
}
