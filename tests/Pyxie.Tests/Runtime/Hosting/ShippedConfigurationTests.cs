using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Pyxie.Tests.Runtime.Git;

using Xunit;

namespace Pyxie.Tests.Runtime.Hosting;

/// <summary>Pyxie is a local, unauthenticated app that stores and runs commands, so a web page that rebinds its own domain to <c>127.0.0.1</c> must not be able to talk to it: the shipped <c>AllowedHosts</c> names only the loopback hosts. The host filtering test serves a request through a real Kestrel built with the same <c>WebApplication.CreateBuilder</c> defaults as <c>Program.cs</c>, but not Pyxie's own pipeline (which would migrate <c>pyxie.db</c> and bind its port), and only the <c>AllowedHosts</c> value is taken from the shipped <c>appsettings.json</c>, which the build copies next to the tests.</summary>
public sealed class ShippedConfigurationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static IConfigurationRoot LoadShippedSettings()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
		Assert.True(File.Exists(path), $"The build copies Pyxie.Web's appsettings.json to {path}.");

		return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
	}

	private static async Task<HttpStatusCode> GetAsync(HttpClient client, int port, string host)
	{
		using HttpRequestMessage request = new(HttpMethod.Get, $"http://127.0.0.1:{port}/");
		request.Headers.Host = host;
		using HttpResponseMessage response = await client.SendAsync(request, Token);

		return response.StatusCode;
	}

	[Fact]
	public void AllowedHosts_OfTheShippedConfiguration_NamesOnlyTheLoopbackHosts()
	{
		string? allowed = LoadShippedSettings()["AllowedHosts"];

		Assert.False(string.IsNullOrWhiteSpace(allowed), "AllowedHosts must be set.");
		string[] hosts = allowed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.DoesNotContain("*", hosts);
		Assert.Equal(["127.0.0.1", "[::1]", "localhost"], hosts.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void EntityFrameworkCommands_OfTheShippedConfiguration_AreLoggedAtWarning()
	{
		string? level = LoadShippedSettings()["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"];

		Assert.Equal("Warning", level);
	}

	[Theory]
	[InlineData("evil.example", HttpStatusCode.BadRequest)]
	[InlineData("evil.example:80", HttpStatusCode.BadRequest)]
	[InlineData("localhost.evil.example", HttpStatusCode.BadRequest)]
	[InlineData("127.0.0.1.evil.example", HttpStatusCode.BadRequest)]
	[InlineData("localhost", HttpStatusCode.OK)]
	[InlineData("localhost:5280", HttpStatusCode.OK)]
	[InlineData("127.0.0.1", HttpStatusCode.OK)]
	[InlineData("127.0.0.1:5280", HttpStatusCode.OK)]
	[InlineData("[::1]:5280", HttpStatusCode.OK)]
	public async Task ARequest_ToAWebAppWithTheShippedAllowedHosts_IsRefusedWithBadRequestUnlessItNamesALoopbackHost(string host, HttpStatusCode expected)
	{
		string? allowedHosts = LoadShippedSettings()["AllowedHosts"];
		await using ScratchFolder contentRoot = ScratchFolder.Create();
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = contentRoot.Folder });
		builder.Logging.ClearProviders();
		builder.Configuration["AllowedHosts"] = allowedHosts;
		builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
		await using WebApplication app = builder.Build();
		app.MapGet("/", () => "Pyxie");
		await app.StartAsync(Token);

		try
		{
			string address = (app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>() ?? throw new InvalidOperationException("The server reports no addresses.")).Addresses.First();
			using HttpClient client = new();

			HttpStatusCode status = await GetAsync(client, new Uri(address).Port, host);

			Assert.Equal(expected, status);
		}
		finally
		{
			await app.StopAsync(CancellationToken.None);
		}
	}
}
