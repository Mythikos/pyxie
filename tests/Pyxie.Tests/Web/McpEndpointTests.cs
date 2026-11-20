using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Projects;
using Pyxie.Runtime.DependencyInjection;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;
using Pyxie.Web.Hosting;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpEndpointTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task ExternalClient_UsesTheGlobalSwitchAndProjectServices()
	{
		await using ScratchFolder scratch = new();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Configuration["Workspace:Root"] = scratch.Folder;
		builder.Services.AddPyxieDatabase(builder.Configuration);
		builder.Services.AddPyxieData();
		builder.Services.AddPyxieRuntime(builder.Configuration);
		builder.Services.AddLocalSandboxes(scratch.Combine("sandboxes"));
		PyxieMcpHosting.Register(builder.Services);
		await using WebApplication app = builder.Build();
		await app.OpenPyxieDatabasesAsync();
		app.UsePyxieRouting();
		app.MapGet("/not-found", () => "Not found page");
		PyxieMcpHosting.Map(app);
		await app.StartAsync(Token);
		Uri endpoint = new(new Uri(Assert.Single(app.Urls)), "/mcp");
		using HttpClient http = new();
		using HttpResponseMessage missingPage = await http.GetAsync(new Uri(endpoint, "/missing-page"), Token);
		Assert.Equal(HttpStatusCode.NotFound, missingPage.StatusCode);
		Assert.Equal("Not found page", await missingPage.Content.ReadAsStringAsync(Token));
		McpAccessSettings access = app.Services.GetRequiredService<McpAccessSettings>();
		Assert.False(access.Enabled, "A new workspace starts with MCP disabled.");
		using HttpResponseMessage disabled = await http.GetAsync(endpoint, Token);
		Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
		Assert.Empty(await disabled.Content.ReadAsStringAsync(Token));
		ResultAssert.Succeeded(access.SetEnabled(true));
		Assert.True(new McpAccessSettings(app.Services.GetRequiredService<WorkspaceLayout>()).Enabled, "Enablement survives a new instance.");
		using HttpRequestMessage foreign = new(HttpMethod.Get, endpoint);
		foreign.Headers.Add("Origin", "https://external.example");
		using HttpResponseMessage rejected = await http.SendAsync(foreign, Token);
		Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

		await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp }), cancellationToken: Token);
		IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Token);
		Assert.Contains(tools, tool => tool.Name == "read_card_media");
		Assert.Contains(tools, tool => tool.Name == "get_harness_setup");
		Assert.Equal("[]", await CallAsync(client, "list_projects", new { }));
		JsonElement imported = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "import_repository", new { folder = source.Folder }));
		int projectId = imported.GetProperty("projectId").GetInt32();
		Assert.Equal(Assert.IsType<Pyxie.Data.Entities.Project>(app.Services.GetRequiredService<OpenProject>().Current).Id, projectId);
		Assert.Contains(TempGitRepository.InitialContent, await CallAsync(client, "read_repository_file", new { projectId, revision = "main", path = "README.md" }), StringComparison.Ordinal);
		JsonElement agent = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "save_agent", new { projectId, name = "DSH", harness = "DeepSeek", authentication = "ApiKey", canWork = true, canReview = true }));
		Assert.Equal("dsh", agent.GetProperty("command").GetString());
		JsonElement settings = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "get_project_settings", new { projectId }));
		JsonElement firstCategory = settings.GetProperty("categories")[0];
		int editedCategoryId = firstCategory.GetProperty("id").GetInt32();
		string? revision = firstCategory.GetProperty("revision").GetString();
		await CallAsync(client, "set_category_color", new { projectId, categoryId = editedCategoryId, color = "#102030", expectedRevision = revision });
		await CallAsync(client, "rename_category", new { projectId, categoryId = editedCategoryId, name = "Stale", expectedRevision = revision }, expectError: true);
		int sandboxVersion = settings.GetProperty("sandbox").GetProperty("version").GetInt32();
		string dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo capture-tools\n";
		await CallAsync(client, "save_dockerfile", new { projectId, dockerfile, expectedVersion = sandboxVersion });
		await CallAsync(client, "save_dockerfile", new { projectId, dockerfile, expectedVersion = sandboxVersion }, expectError: true);
		int buildId = JsonSerializer.Deserialize<int>(await CallAsync(client, "build_project_image", new { projectId }));
		await app.Services.GetRequiredService<SandboxImageBuilds>().Running.WaitAsync(Token);
		JsonElement build = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "get_image_build", new { projectId, buildId }));
		Assert.Equal(LocalSandboxes.Image, build.GetProperty("image").GetString());
		Assert.Equal(buildId, build.GetProperty("build").GetProperty("id").GetInt32());
		await CallAsync(client, "get_image_build", new { projectId, buildId = buildId + 1 }, expectError: true);
		int categoryId = settings.GetProperty("categories")[0].GetProperty("id").GetInt32();
		string created = await CallAsync(client, "create_card", new { projectId, name = "Capture", description = "Attach a screenshot.", categoryId, workerAgentId = agent.GetProperty("id").GetInt32() });
		Assert.Contains("Queue", created, StringComparison.Ordinal);
		Assert.Equal("0", await CallAsync(client, "close_completed_cards", new { projectId }));
		Assert.Contains("Capture", await CallAsync(client, "get_board", new { projectId }), StringComparison.Ordinal);
		Assert.Contains("Complete", await CallAsync(client, "close_card", new { projectId, cardId = 1 }, expectError: true), StringComparison.OrdinalIgnoreCase);

		string file = await CallAsync(client, "export_settings", new { projectId });
		JsonElement preview = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "preview_settings_import", new { projectId, file }));
		string? categoryPreview = preview.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("section").GetString() == "Categories").GetProperty("previewId").GetString();
		var sections = new { Categories = categoryPreview };
		await CallAsync(client, "save_gate", new { projectId, gate = new { name = "Compile", command = "make", arguments = "", timeoutMinutes = 5, isEnabled = true } });
		await CallAsync(client, "import_settings", new { projectId, file, sections });
		await CallAsync(client, "create_category", new { projectId, name = "UI", color = "#00CC99" });
		await CallAsync(client, "import_settings", new { projectId, file, sections }, expectError: true);
		string newerFile = file.Replace("#102030", "#112233", StringComparison.Ordinal);
		JsonElement changedPreview = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "preview_settings_import", new { projectId, file = newerFile }));
		var changedSections = new { Categories = changedPreview.GetProperty("sections").EnumerateArray().Single(section => section.GetProperty("section").GetString() == "Categories").GetProperty("previewId").GetString() };
		JsonElement latest = JsonSerializer.Deserialize<JsonElement>(await CallAsync(client, "get_project_settings", new { projectId }));
		string? latestRevision = latest.GetProperty("categories").EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == editedCategoryId).GetProperty("revision").GetString();
		await CallAsync(client, "set_category_color", new { projectId, categoryId = editedCategoryId, color = "#445566", expectedRevision = latestRevision });
		await CallAsync(client, "import_settings", new { projectId, file = newerFile, sections = changedSections }, expectError: true);
		await CallAsync(client, "close_project", new { projectId });
		await CallAsync(client, "create_category", new { projectId, name = "Wrong project", color = "#001122" }, expectError: true);
		ResultAssert.Succeeded(access.SetEnabled(false));
		using HttpResponseMessage revoked = await http.GetAsync(endpoint, Token);
		Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
		await app.StopAsync(Token);
	}

	private static async Task<string> CallAsync(McpClient client, string name, object input, bool expectError = false)
	{
		Dictionary<string, object?> arguments = JsonSerializer.SerializeToElement(input).EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value);
		CallToolResult result = await client.CallToolAsync(name, arguments, cancellationToken: Token);
		string message = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
		Assert.True((result.IsError ?? false) == expectError, $"{name}: {message}");
		return message;
	}
}
