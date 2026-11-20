using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Runtime.Media;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Board;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class MediaEndpointTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task Upload_RequiresAntiforgeryAndDownloadChecksOwnershipAndSupportsRanges()
	{
		await using MediaRig rig = new();
		CardMediaSession session = await rig.BeginAsync();
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Services.AddAntiforgery();
		builder.Services.AddSingleton(rig.Open);
		builder.Services.AddSingleton(rig.Storage);
		builder.Services.AddSingleton<IProjectDbContextSource>(rig.Pipeline.Database.ContextSource);
		builder.Services.AddSingleton<TimeProvider>(rig.Pipeline.Database.Clock);
		await using WebApplication app = builder.Build();
		Type mapper = typeof(CardMedia).Assembly.GetType("Pyxie.Web.Hosting.PyxieMediaEndpoints") ?? throw new InvalidOperationException("Media endpoints are missing.");
		MethodInfo method = mapper.GetMethod("MapPyxieMedia", BindingFlags.Public | BindingFlags.Static) ?? throw new InvalidOperationException("Media endpoint mapping is missing.");
		method.Invoke(null, new object[] { (IEndpointRouteBuilder)app });
		await app.StartAsync(Token);
		using HttpClient client = new(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(Assert.Single(app.Urls)) };
		string uploadUrl = $"/media/1/uploads/{session.UploadId}?name=context.html";
		using HttpResponseMessage refused = await client.PostAsync(uploadUrl, new StringContent("0123456789"), Token);
		Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
		JsonElement token = await client.GetFromJsonAsync<JsonElement>("/media/token", Token);
		client.DefaultRequestHeaders.Add(token.GetProperty("header").GetString() ?? "RequestVerificationToken", token.GetProperty("token").GetString());
		using HttpResponseMessage accepted = await client.PostAsync(uploadUrl, new StringContent("0123456789"), Token);
		accepted.EnsureSuccessStatusCode();
		MediaFile file = await accepted.Content.ReadFromJsonAsync<MediaFile>(Token) ?? throw new InvalidOperationException("No file returned.");
		using HttpResponseMessage staged = await client.GetAsync($"/media/1/files/{file.Id}?upload={session.UploadId}", Token);
		Assert.Equal(HttpStatusCode.OK, staged.StatusCode);
		Assert.Equal("application/octet-stream", staged.Content.Headers.ContentType?.MediaType);
		Assert.Equal("attachment", staged.Content.Headers.ContentDisposition?.DispositionType);
		Assert.Contains("nosniff", staged.Headers.GetValues("X-Content-Type-Options"));
		using HttpResponseMessage crossSession = await client.GetAsync($"/media/1/files/{file.Id}?upload={Guid.NewGuid()}", Token);
		Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
		CardInput input = await TestCardInput.CreateAsync(rig.Pipeline.Database, "Card");
		WorkCard card = ResultAssert.Succeeded(await rig.Pipeline.Cards.CreateAsync(input with { Media = new MediaEdit(session.UploadId, [file.Id]) }, Token));
		using HttpRequestMessage range = new(HttpMethod.Get, $"/media/1/files/{file.Id}?card={card.Id}");
		range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, 4);
		using HttpResponseMessage partial = await client.SendAsync(range, Token);
		Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
		Assert.Equal("234", await partial.Content.ReadAsStringAsync(Token));
		using HttpResponseMessage wrongCard = await client.GetAsync($"/media/1/files/{file.Id}?card={card.Id + 1}", Token);
		Assert.Equal(HttpStatusCode.NotFound, wrongCard.StatusCode);
		using HttpResponseMessage wrongProject = await client.GetAsync($"/media/2/files/{file.Id}?card={card.Id}", Token);
		Assert.Equal(HttpStatusCode.NotFound, wrongProject.StatusCode);
		await app.StopAsync(Token);
	}
}
