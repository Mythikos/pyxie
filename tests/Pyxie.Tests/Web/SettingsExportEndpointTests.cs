using System.Reflection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>The Export link downloads the settings of the project it names as a settings file named after the project, and nothing when that project is not the open one. The endpoint is mapped as <c>Program.cs</c> maps it and its request delegate is called with a request of the test's own, without a server: Pyxie.Web keeps its hosting extensions internal, so the mapping is reached by name.</summary>
public sealed class SettingsExportEndpointTests
{
	private const string ExportRoute = "settings/export";

	[Fact]
	public async Task Export_OfTheOpenProject_DownloadsItsSettingsAsASettingsFileNamedAfterIt()
	{
		await using TestDatabase database = new();
		await TestData.AddAgentAsync(database, "Claude", canWork: true, canReview: true);
		OpenProject openProject = StandInProject.Open();
		Project project = openProject.Current ?? throw new InvalidOperationException("The stand-in project should be open.");

		(int status, string? contentType, string? disposition, byte[] body) = await GetAsync(database, openProject, $"?project={project.Id}");

		Assert.Equal(StatusCodes.Status200OK, status);
		Assert.Equal(SettingsFileFormat.MediaType, contentType);
		Assert.True(ContentDispositionHeaderValue.TryParse(disposition, out ContentDispositionHeaderValue? parsed), $"The download should name its file, but its Content-Disposition was '{disposition}'.");
		Assert.Equal($"{project.Name}{SettingsFileFormat.FileNameSuffix}", parsed.FileName.Value?.Trim('"'));
		ProjectSettings settings = ResultAssert.Succeeded(SettingsFileReader.Read(body));
		ProjectSettings expected = ResultAssert.Succeeded(await SettingsTransferHost.NewService(database).ExportAsync(project.Id, TestContext.Current.CancellationToken));
		Assert.Equal([Agent("Claude")], settings.Agents);
		Assert.Equal(expected.Gates, settings.Gates);
		Assert.Equal(expected.Categories, settings.Categories);
		Assert.Equal(expected.Sandbox, settings.Sandbox);
	}

	[Theory]
	[InlineData(true, "?project=2")]
	[InlineData(false, "?project=1")]
	public async Task Export_OfAProjectThatIsNotTheOpenOne_IsNotFound(bool aProjectIsOpen, string query)
	{
		await using TestDatabase database = new();
		OpenProject openProject = aProjectIsOpen ? StandInProject.Open() : new OpenProject();

		(int status, _, _, byte[] body) = await GetAsync(database, openProject, query);

		Assert.Equal(StatusCodes.Status404NotFound, status);
		Assert.Empty(body);
	}

	// Maps the export as Program.cs does, over the real settings transfer service on the test database, and sends it a GET with the query.
	private static async Task<(int Status, string? ContentType, string? Disposition, byte[] Body)> GetAsync(TestDatabase database, OpenProject openProject, string query)
	{
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.Services.AddSingleton(openProject);
		builder.Services.AddSingleton<ISettingsTransferService>(new SettingsTransferService(database.ContextSource, openProject, database.Clock));
		await using WebApplication app = builder.Build();
		MapSettingsExport(app);
		RouteEndpoint endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(), candidate => candidate.RoutePattern.RawText == ExportRoute);
		RequestDelegate handle = endpoint.RequestDelegate ?? throw new InvalidOperationException("The export endpoint has no request delegate.");

		await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
		using MemoryStream body = new();
		DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
		context.Request.Method = HttpMethods.Get;
		context.Request.Path = $"/{ExportRoute}";
		context.Request.QueryString = new QueryString(query);
		context.Response.Body = body;

		await handle(context);

		return (context.Response.StatusCode, context.Response.ContentType, context.Response.Headers.ContentDisposition.ToString(), body.ToArray());
	}

	private static void MapSettingsExport(IEndpointRouteBuilder endpoints)
	{
		Type extensions = typeof(SettingsTransfer).Assembly.GetType("Pyxie.Web.Hosting.PyxieEndpointRouteBuilderExtensions") ?? throw new InvalidOperationException("Pyxie.Web no longer has PyxieEndpointRouteBuilderExtensions.");
		MethodInfo map = extensions.GetMethod("MapPyxieSettingsExport", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(extensions.Name, "MapPyxieSettingsExport");

		map.Invoke(null, [endpoints]);
	}
}
