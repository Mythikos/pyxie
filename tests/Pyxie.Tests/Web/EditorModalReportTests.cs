using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>The agent and gate editors take what their modal reports through VisibleChanged only as a close the person made, with Escape or the backdrop. The modal reports once its 150 ms animation has ended, so it also repeats, late, the opening and the closing the parent asked for: an opening it repeats after the editor saved and closed would show the editor again, ready to save again, and a closing it repeats is already done.</summary>
public sealed class EditorModalReportTests
{
	[Fact]
	public async Task AgentEditor_ModalsReportThatItOpened_IsNotPassedOn()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<AgentEditor> editor = await OpenAgentEditorAsync(database);

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", true);

		Assert.Empty(editor.Reported);
	}

	[Fact]
	public async Task AgentEditor_ModalsReportThatThePersonClosedIt_IsPassedOnAndLeavesNothingToSave()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<AgentEditor> editor = await OpenAgentEditorAsync(database);
		FillAgent(editor);

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", false);
		await editor.Rendered.ClickAsync("SaveAsync");

		Assert.Equal([false], editor.Reported);
		Assert.Empty(await new AgentService(database.ContextFactory, database.Clock).ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task AgentEditor_ModalsLateReportOfTheCloseASaveMade_IsNotPassedOnAgain()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<AgentEditor> editor = await OpenAgentEditorAsync(database);
		FillAgent(editor);
		await editor.Rendered.ClickAsync("SaveAsync");

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", false);

		Assert.Equal([false], editor.Reported);
	}

	[Fact]
	public async Task GateEditor_ModalsReportThatItOpened_IsNotPassedOn()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<GateEditor> editor = await OpenGateEditorAsync(database);

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", true);

		Assert.Empty(editor.Reported);
	}

	[Fact]
	public async Task GateEditor_ModalsReportThatThePersonClosedIt_IsPassedOnAndLeavesNothingToSave()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<GateEditor> editor = await OpenGateEditorAsync(database);
		FillGate(editor);

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", false);
		await editor.Rendered.ClickAsync("SaveAsync");

		Assert.Equal([false], editor.Reported);
		Assert.Empty(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task GateEditor_ModalsLateReportOfTheCloseASaveMade_IsNotPassedOnAgain()
	{
		await using TestDatabase database = new();
		await using EditorUnderTest<GateEditor> editor = await OpenGateEditorAsync(database);
		FillGate(editor);
		await editor.Rendered.ClickAsync("SaveAsync");

		await editor.Rendered.ClickAsync("OnModalVisibleChangedAsync", false);

		Assert.Equal([false], editor.Reported);
	}

	private static async Task<EditorUnderTest<AgentEditor>> OpenAgentEditorAsync(TestDatabase database)
	{
		return await EditorUnderTest<AgentEditor>.OpenAsync(services => services.AddSingleton<IAgentService>(new AgentService(database.ContextFactory, database.Clock)));
	}

	private static async Task<EditorUnderTest<GateEditor>> OpenGateEditorAsync(TestDatabase database)
	{
		return await EditorUnderTest<GateEditor>.OpenAsync(services => services.AddSingleton<IGateService>(new GateService(database.ContextFactory)));
	}

	private static void FillAgent(EditorUnderTest<AgentEditor> editor)
	{
		AgentFormModel model = editor.Rendered.ReadField<AgentFormModel>("_model");
		model.Name = "Claude";
		model.Command = "claude";
	}

	private static void FillGate(EditorUnderTest<GateEditor> editor)
	{
		GateFormModel model = editor.Rendered.ReadField<GateFormModel>("_model");
		model.Name = "Format";
		model.Command = "dotnet";
	}
}
