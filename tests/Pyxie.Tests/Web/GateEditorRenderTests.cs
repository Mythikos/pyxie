using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GateEditorRenderTests
{
	[Fact]
	public async Task AddingStartsFromBlankTextFieldsAndAnEnabledGate()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<GateEditor>(database, Parameters(null));

		Assert.Contains(">Add gate<", html);
		Assert.Equal(string.Empty, FieldValue(html, "Name"));
		Assert.Equal(string.Empty, FieldValue(html, "Command"));
		Assert.Equal(string.Empty, FieldValue(html, "Arguments"));
		Assert.Equal("true", SwitchState(html));
	}

	[Fact]
	public async Task EditingStartsFromTheStoredGate()
	{
		await using TestDatabase database = new();
		Gate gate = await CreateAsync(database, GateTestInput.Create("Format", "dotnet", "format --verify-no-changes", 5, isEnabled: false));

		string html = await GateRenderHost.RenderAsync<GateEditor>(database, Parameters(gate));

		Assert.Contains(">Edit gate<", html);
		Assert.Equal("Format", FieldValue(html, "Name"));
		Assert.Equal("dotnet", FieldValue(html, "Command"));
		Assert.Equal("format --verify-no-changes", FieldValue(html, "Arguments"));
		Assert.Equal("false", SwitchState(html));
	}

	[Fact]
	public async Task NamesTheEnabledSwitchSoItIsNotAnUnnamedControl()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<GateEditor>(database, Parameters(null));

		Assert.Equal("Enabled", GateHtml.AttributeValue(GateHtml.OpenTag(html, "button", "name=\"_model.IsEnabled\""), "aria-label"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ShowsNoFieldMessagesBeforeTheFirstSaveAttempt(bool editing)
	{
		await using TestDatabase database = new();
		Gate? gate = editing ? await CreateAsync(database, GateTestInput.Create()) : null;

		string html = await GateRenderHost.RenderAsync<GateEditor>(database, Parameters(gate));

		Assert.DoesNotContain("field-error", html);
		Assert.DoesNotContain("is required", html);
	}

	[Fact]
	public async Task OffersCancelAndSave()
	{
		await using TestDatabase database = new();

		string html = await GateRenderHost.RenderAsync<GateEditor>(database, Parameters(null));

		Assert.Equal(1, GateHtml.CountButtons(html, "Cancel"));
		Assert.Equal(1, GateHtml.CountButtons(html, "Save"));
	}

	private static Dictionary<string, object?> Parameters(Gate? gate)
	{
		return new Dictionary<string, object?> { ["Visible"] = true, ["Gate"] = gate };
	}

	private static string FieldValue(string html, string label)
	{
		string input = GateHtml.OpenTag(html, "input", $"name=\"_model.{label}\"");

		return GateHtml.AttributeValue(input, "value") ?? string.Empty;
	}

	private static string? SwitchState(string html)
	{
		return GateHtml.AttributeValue(GateHtml.OpenTag(html, "button", "name=\"_model.IsEnabled\""), "aria-checked");
	}

	private static async Task<Gate> CreateAsync(TestDatabase database, GateInput input)
	{
		ServiceResult<Gate> result = await new GateService(database.ContextFactory).CreateAsync(input, TestContext.Current.CancellationToken);
		Assert.True(result.Succeeded, result.Error);

		return result.Value!;
	}
}
