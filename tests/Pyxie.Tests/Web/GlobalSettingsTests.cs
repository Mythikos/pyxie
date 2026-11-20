using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class GlobalSettingsTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task RefusedSwitchRestoresTheStoredStateAndCanBeRetried(bool enabled)
	{
		Assert.SkipWhen(enabled && !OperatingSystem.IsWindows(), "Windows refuses to delete an open marker without delete sharing.");
		await using ScratchFolder scratch = new();
		McpAccessSettings access = new(new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = scratch.Folder })));
		string marker = enabled ? scratch.CreateFile("mcp.enabled") : scratch.CreateSubfolder("mcp.enabled");
		await using ComponentHarness harness = new(services =>
		{
			services.AddSingleton(access);
			services.AddSingleton<IJSRuntime, GateStubJsRuntime>();
		});
		await using GateInteractiveHost page = new(harness.Services);
		await page.StartAsync<GlobalSettings>();
		GateRenderedElement before = await SwitchAsync(page);

		using (FileStream? held = enabled ? File.OpenRead(marker) : null)
		{
			await page.SetCheckedAsync(before, !enabled);
		}

		GateRenderedElement restored = await SwitchAsync(page);
		Assert.Equal(enabled, access.Enabled);
		Assert.Equal(enabled, restored.Attribute("checked") is not null);
		Assert.NotEqual(before.EventHandlers["onchange"], restored.EventHandlers["onchange"]);
		Assert.Equal(enabled, (await page.FindElementsAsync("input", input => input.Attribute("id") == "mcp-uri")).Count == 1);
		Assert.Contains("Could not save MCP access", Assert.Single(await page.AlertsAsync()), StringComparison.Ordinal);

		if (!enabled)
		{
			Directory.Delete(marker);
		}

		await page.SetCheckedAsync(restored, !enabled);

		Assert.Equal(!enabled, access.Enabled);
		Assert.Equal(!enabled, (await SwitchAsync(page)).Attribute("checked") is not null);
		Assert.Empty(await page.AlertsAsync());
	}

	private static async Task<GateRenderedElement> SwitchAsync(GateInteractiveHost page)
	{
		return Assert.Single(await page.FindElementsAsync("input", input => input.Attribute("id") == "mcp-enabled"));
	}
}
