using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>Serves a folder over plain HTTP on a loopback port of its own, which is all git needs to clone a repository the dumb way (<c>git update-server-info</c> in a bare repository writes the files it reads). It gives a test a URL that clones for real, so the messages that name the URL can be checked without a network or a credential.</summary>
public sealed class StaticFileHttpServer : IAsyncDisposable
{
	private readonly WebApplication _app;

	private StaticFileHttpServer(WebApplication app, int port)
	{
		_app = app;
		Port = port;
	}

	/// <summary>Gets the loopback port the server listens on.</summary>
	public int Port { get; }

	/// <summary>Gets the address of the server, without a trailing slash and without credentials.</summary>
	public string BaseAddress => $"http://127.0.0.1:{Port}";

	/// <summary>Starts a server for a folder.</summary>
	/// <param name="folder">The folder to serve. Its files are served whatever their type.</param>
	/// <returns>The running server.</returns>
	public static async Task<StaticFileHttpServer> StartAsync(string folder)
	{
		WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
		WebApplication app = builder.Build();
		app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(folder), ServeUnknownFileTypes = true, DefaultContentType = "application/octet-stream" });

		await app.StartAsync();

		IServerAddressesFeature addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException("The server reports no addresses.");

		return new StaticFileHttpServer(app, new Uri(addresses.Addresses.First()).Port);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _app.StopAsync();
		await _app.DisposeAsync();
	}
}
