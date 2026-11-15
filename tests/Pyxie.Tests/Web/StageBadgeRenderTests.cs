using Pyxie.Data.Enums;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class StageBadgeRenderTests
{
	[Theory]
	[InlineData(RoundKind.Work, "Work")]
	[InlineData(RoundKind.Review, "Review")]
	public async Task Render_ShowsAWorkOrReviewRoundAsAGreenChipWithALiveDot(RoundKind kind, string text)
	{
		string html = await RenderAsync(kind);

		Assert.Matches($@"^<span class=""pyx-chip pyx-chip-green""[^>]*><span class=""stage-dot""[^>]*aria-hidden=""true""[^>]*></span>{text}</span>$", html);
	}

	[Fact]
	public async Task Render_ShowsAResolveRoundAsAPinkChipWithTheMergeIcon()
	{
		string html = await RenderAsync(RoundKind.Resolve);

		Assert.Matches(@"^<span class=""pyx-chip pyx-chip-pink stage-resolve""[^>]*><span class=""anticon merge""><svg [^>]*aria-hidden=""true""", html);
		Assert.EndsWith("Resolve</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsAMergeRoundAsADarkChipWithThePyxieMarkSinceOnlyPyxieMerges()
	{
		string html = await RenderAsync(RoundKind.Merge);

		Assert.Matches(@"^<span class=""pyx-chip pyx-chip-dark stage-merging""[^>]*><img class=""pyxie-mark"" src=""brand/pyxie-mark-small\.svg"" width=""14"" height=""14"" alt=""""", html);
		Assert.EndsWith("Merging</span>", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsNoBadgeForAGateRoundBecauseTheLaneSaysGates()
	{
		string html = await RenderAsync(RoundKind.Gate);

		Assert.Equal(string.Empty, html);
	}

	private static async Task<string> RenderAsync(RoundKind kind)
	{
		await using ComponentHarness harness = new(_ => { });
		RenderedComponent<StageBadge> badge = await harness.RenderAsync<StageBadge>(new Dictionary<string, object> { [nameof(StageBadge.Kind)] = kind });

		return (await badge.HtmlAsync()).Trim();
	}
}
