using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Conventions;

public sealed class RazorMarkupScannerTests
{
	[Fact]
	public void FindComponents_ReportsNamesAttributesAndLines()
	{
		const string markup = """
			<Field>
				<Check TValue="bool" @bind-Value="_model.CanWork" Disabled>Can work</Check>
			</Field>
			""";

		IReadOnlyList<RazorComponentUsage> usages = RazorMarkupScanner.FindComponents(markup);

		Assert.Equal(["Field", "Check"], usages.Select(usage => usage.Name));
		Assert.Equal([1, 2], usages.Select(usage => usage.Line));
		Assert.Equal(["TValue", "@bind-Value", "Disabled"], usages[1].Attributes);
	}

	[Fact]
	public void FindComponents_KeepsQuotedCSharpExpressionsInsideAttributeValues()
	{
		const string markup = """<Button Clicked="@(() => Handle("a > b", 'c'))" Color="Color.Primary">Go</Button>""";

		RazorComponentUsage usage = Assert.Single(RazorMarkupScanner.FindComponents(markup));

		Assert.Equal(["Clicked", "Color"], usage.Attributes);
	}

	[Fact]
	public void FindComponents_ReadsMultilineAndSelfClosingElements()
	{
		const string markup = """
			<TextInput
				@bind-Value="_name"
				Placeholder="Name" /><Badge Color="Color.Info">x</Badge>
			""";

		IReadOnlyList<RazorComponentUsage> usages = RazorMarkupScanner.FindComponents(markup);

		Assert.Equal(["TextInput", "Badge"], usages.Select(usage => usage.Name));
		Assert.Equal(["@bind-Value", "Placeholder"], usages[0].Attributes);
	}

	[Fact]
	public void FindComponents_SkipsCommentsAndPlainHtml()
	{
		const string markup = """
			@* <Hidden Attribute="x" /> *@
			<!-- <AlsoHidden /> -->
			<div class="x"><span>text</span></div>
			<Shown />
			""";

		Assert.Equal(["Shown"], RazorMarkupScanner.FindComponents(markup).Select(usage => usage.Name));
	}
}
