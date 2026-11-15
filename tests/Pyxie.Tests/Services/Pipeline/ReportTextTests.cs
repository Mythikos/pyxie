using System.Globalization;
using System.Text.RegularExpressions;

using Pyxie.Data.Services.Pipeline;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>Shortened text always fits its limit, keeps the parts that matter, and says how much was cut exactly once.</summary>
public sealed class ReportTextTests
{
	private static readonly Regex s_truncationMarker = new(@"\[truncated: (\d+) characters omitted\]", RegexOptions.CultureInvariant);

	private static string Numbered(int length)
	{
		return string.Concat(Enumerable.Range(0, length).Select(index => (char)('a' + (index % 26))));
	}

	private static int OmittedCount(string shortened)
	{
		Match marker = s_truncationMarker.Match(shortened);
		Assert.True(marker.Success, "The shortened text must carry a truncation marker.");

		return int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	private static int MarkerCount(string text)
	{
		return s_truncationMarker.Count(text);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(500)]
	[InlineData(1_000)]
	public void Excerpt_OfTextThatFits_IsReturnedAsItIs(int length)
	{
		string value = Numbered(length);

		Assert.Same(value, ReportText.Excerpt(value, 1_000, 300));
	}

	[Theory]
	[InlineData(1_001)]
	[InlineData(1_064)]
	[InlineData(5_000)]
	[InlineData(250_000)]
	public void Excerpt_OfTextThatDoesNotFit_NeverExceedsTheLimitAndKeepsTheBeginningAndTheEnd(int length)
	{
		string value = Numbered(length);

		string excerpt = ReportText.Excerpt(value, 1_000, 300);

		Assert.True(excerpt.Length <= 1_000, $"The excerpt is {excerpt.Length} characters.");
		Assert.StartsWith(value[..300], excerpt, StringComparison.Ordinal);
		int tail = 1_000 - 300 - 64;
		Assert.EndsWith(value[^tail..], excerpt, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(1_001)]
	[InlineData(5_000)]
	[InlineData(250_000)]
	public void Excerpt_SaysExactlyHowManyCharactersItLeftOutAndOnlyOnce(int length)
	{
		string value = Numbered(length);

		string excerpt = ReportText.Excerpt(value, 1_000, 300);

		Assert.Equal(1, MarkerCount(excerpt));
		int tail = 1_000 - 300 - 64;
		Assert.Equal(length - 300 - tail, OmittedCount(excerpt));
		Assert.Equal("\n[truncated: " + OmittedCount(excerpt).ToString(CultureInfo.InvariantCulture) + " characters omitted]\n", excerpt[300..^tail]);
	}

	[Fact]
	public void Excerpt_OfTextOneLongerThanTheLimit_CutsAndOfTextExactlyAtTheLimitDoesNot()
	{
		Assert.Equal(0, MarkerCount(ReportText.Excerpt(Numbered(1_000), 1_000, 300)));
		Assert.Equal(1, MarkerCount(ReportText.Excerpt(Numbered(1_001), 1_000, 300)));
	}

	[Fact]
	public void Excerpt_WithTheLimitAndHeadTheBriefingUses_KeepsFiveThousandCharactersAndTheEndWithinFifteenThousand()
	{
		string value = Numbered(50_000);

		string excerpt = ReportText.Excerpt(value, 15_000, 5_000);

		Assert.True(excerpt.Length <= 15_000, $"The excerpt is {excerpt.Length} characters.");
		Assert.StartsWith(value[..5_000] + "\n[truncated: 35064 characters omitted]\n", excerpt, StringComparison.Ordinal);
		Assert.EndsWith(value[^9_936..], excerpt, StringComparison.Ordinal);
	}

	[Fact]
	public void Excerpt_WhenTheLimitLeavesRoomForOnlyTheMarker_KeepsTheHeadAndNoEnd()
	{
		string value = Numbered(10_000);

		string excerpt = ReportText.Excerpt(value, maxLength: 364, headLength: 300);

		Assert.True(excerpt.Length <= 364, $"The excerpt is {excerpt.Length} characters.");
		Assert.StartsWith(value[..300], excerpt, StringComparison.Ordinal);
		Assert.EndsWith(" characters omitted]\n", excerpt, StringComparison.Ordinal);
		Assert.Equal(1, MarkerCount(excerpt));
		Assert.Equal(10_000 - 300, OmittedCount(excerpt));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(500)]
	[InlineData(1_000)]
	public void Truncate_OfTextThatFits_IsReturnedAsItIs(int length)
	{
		string value = Numbered(length);

		Assert.Same(value, ReportText.Truncate(value, 1_000));
	}

	[Theory]
	[InlineData(1_001)]
	[InlineData(1_064)]
	[InlineData(5_000)]
	[InlineData(250_000)]
	public void Truncate_OfTextThatDoesNotFit_NeverExceedsTheLimitAndKeepsTheBeginning(int length)
	{
		string value = Numbered(length);

		string truncated = ReportText.Truncate(value, 1_000);

		Assert.True(truncated.Length <= 1_000, $"The truncated text is {truncated.Length} characters.");
		Assert.StartsWith(value[..(1_000 - 64)], truncated, StringComparison.Ordinal);
		Assert.EndsWith(" characters omitted]", truncated, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(1_001)]
	[InlineData(5_000)]
	[InlineData(250_000)]
	public void Truncate_SaysExactlyHowManyCharactersItLeftOutAndOnlyOnce(int length)
	{
		string value = Numbered(length);

		string truncated = ReportText.Truncate(value, 1_000);

		Assert.Equal(1, MarkerCount(truncated));
		Assert.Equal(length - (1_000 - 64), OmittedCount(truncated));
		Assert.Equal(1_000 - 64 + "\n[truncated: ".Length + OmittedCount(truncated).ToString(CultureInfo.InvariantCulture).Length + " characters omitted]".Length, truncated.Length);
	}

	[Fact]
	public void Truncate_OfTextOneLongerThanTheLimit_CutsAndOfTextExactlyAtTheLimitDoesNot()
	{
		Assert.Equal(0, MarkerCount(ReportText.Truncate(Numbered(1_000), 1_000)));
		Assert.Equal(1, MarkerCount(ReportText.Truncate(Numbered(1_001), 1_000)));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(150)]
	[InlineData(200)]
	public void Clip_OfTextThatFits_IsReturnedAsItIs(int length)
	{
		string value = Numbered(length);

		Assert.Same(value, ReportText.Clip(value, 200));
	}

	[Theory]
	[InlineData(201)]
	[InlineData(1_000)]
	[InlineData(250_000)]
	public void Clip_OfTextThatDoesNotFit_IsExactlyTheLimitLongAndEndsInAnEllipsisWithoutAMarker(int length)
	{
		string value = Numbered(length);

		string clipped = ReportText.Clip(value, 200);

		Assert.Equal(200, clipped.Length);
		Assert.Equal(value[..199] + "…", clipped);
		Assert.Equal(0, MarkerCount(clipped));
	}

	[Fact]
	public void Clip_OfALimitOfOne_KeepsOnlyTheEllipsis()
	{
		Assert.Equal("…", ReportText.Clip("abc", 1));
		Assert.Equal("a", ReportText.Clip("a", 1));
	}
}
