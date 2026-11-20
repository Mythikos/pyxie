using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;
using Pyxie.Tests.Support;
using Pyxie.Web.Hosting;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpRequestCancellationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task DisconnectDuringPost_CancelsHandlerAndCompletesStartedResponseWithoutAnException()
	{
		await using ScratchFolder scratch = new();
		TaskCompletionSource handlerStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource handlerCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<(bool Started, int Status)> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		builder.Services.AddSingleton(EnabledAccess(scratch));
		builder.Services.AddMcpServer().WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
			.WithListToolsHandler(async (_, token) =>
			{
				handlerStarted.TrySetResult();
				try
				{
					await Task.Delay(Timeout.InfiniteTimeSpan, token);
				}
				catch (OperationCanceledException) when (token.IsCancellationRequested)
				{
					handlerCanceled.TrySetResult();
					throw;
				}

				return new ListToolsResult { Tools = [] };
			});
		await using WebApplication app = builder.Build();
		app.Use(async (context, next) =>
		{
			try
			{
				await next(context);
				finished.TrySetResult((context.Response.HasStarted, context.Response.StatusCode));
			}
			catch (Exception exception)
			{
				finished.TrySetException(exception);
				throw;
			}
		});
		app.UsePyxieRouting();
		PyxieMcpHosting.Map(app);
		await app.StartAsync(Token);
		Uri endpoint = new(new Uri(Assert.Single(app.Urls)), "/mcp");
		using HttpClient client = new(new SocketsHttpHandler { MaxResponseDrainSize = 0 });
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
		client.DefaultRequestHeaders.Add("MCP-Protocol-Version", "2025-11-25");
		using CancellationTokenSource disconnect = CancellationTokenSource.CreateLinkedTokenSource(Token);
		Task<HttpResponseMessage> pending = client.PostAsJsonAsync(endpoint, new { jsonrpc = "2.0", id = 1, method = "tools/list" }, disconnect.Token);
		await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
		await disconnect.CancelAsync();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
		Assert.Equal((true, StatusCodes.Status200OK), await finished.Task.WaitAsync(TimeSpan.FromSeconds(15), Token));
		await handlerCanceled.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);

		using HttpResponseMessage ping = await client.PostAsJsonAsync(endpoint, new { jsonrpc = "2.0", id = 2, method = "ping" }, Token);
		Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
		Assert.Contains("\"result\"", await ping.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
		await app.StopAsync(Token);
	}

	[Fact]
	public async Task DisconnectBeforeResponseStarts_RecordsClientClosedRequest()
	{
		await using ScratchFolder scratch = new();
		await using ServiceProvider services = new ServiceCollection().AddSingleton(EnabledAccess(scratch)).BuildServiceProvider();
		DefaultHttpContext http = LocalRequest(services, "/mcp", aborted: true);
		await PyxieMcpHosting.GuardAsync(http, context => Task.FromCanceled(context.RequestAborted));
		Assert.Equal(StatusCodes.Status499ClientClosedRequest, http.Response.StatusCode);
	}

	[Theory]
	[InlineData("/mcp", false, true)]
	[InlineData("/board", true, true)]
	[InlineData("/mcp", true, false)]
	public async Task OtherFailuresReachTheCaller(string path, bool aborted, bool canceledException)
	{
		await using ScratchFolder scratch = new();
		await using ServiceProvider services = new ServiceCollection().AddSingleton(EnabledAccess(scratch)).BuildServiceProvider();
		DefaultHttpContext http = LocalRequest(services, path, aborted);
		Exception failure = canceledException ? new TaskCanceledException() : new InvalidOperationException();
		Assert.Same(failure, await Record.ExceptionAsync(() => PyxieMcpHosting.GuardAsync(http, _ => Task.FromException(failure))));
	}

	private static McpAccessSettings EnabledAccess(ScratchFolder scratch)
	{
		McpAccessSettings access = new(new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = scratch.Folder })));
		ResultAssert.Succeeded(access.SetEnabled(true));
		return access;
	}

	private static DefaultHttpContext LocalRequest(IServiceProvider services, string path, bool aborted)
	{
		DefaultHttpContext http = new() { RequestServices = services, RequestAborted = new CancellationToken(aborted) };
		http.Connection.RemoteIpAddress = IPAddress.Loopback;
		http.Request.Host = new HostString("localhost");
		http.Request.Path = path;
		return http;
	}
}
