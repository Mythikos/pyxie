using System.Net;

using Microsoft.AspNetCore.Http;

using Pyxie.Web.Hosting;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class McpLocalAccessTests
{
	[Theory]
	[InlineData("127.0.0.1", "localhost:5280", null, true)]
	[InlineData("::ffff:127.0.0.1", "[::1]:5280", "http://[::1]:5280", true)]
	[InlineData("::1", "localhost:5280", "http://localhost:5280", true)]
	[InlineData("::1", "localhost:80", "http://localhost", true)]
	[InlineData("::1", "[::1]:443", "https://[::1]", true, "https")]
	[InlineData("::1", "localhost:8080", "http://localhost", false)]
	[InlineData("192.0.2.1", "localhost:5280", null, false)]
	[InlineData("127.0.0.1", "external.example", null, false)]
	[InlineData("127.0.0.1", "localhost:5280", "http://localhost:8100", false)]
	[InlineData("127.0.0.1", "localhost:5280", "null", false)]
	public void AccessChecksThePeerHostAndBrowserOrigin(string peer, string host, string? origin, bool allowed, string scheme = "http")
	{
		DefaultHttpContext http = new();
		http.Connection.RemoteIpAddress = IPAddress.Parse(peer);
		http.Request.Scheme = scheme;
		http.Request.Host = HostString.FromUriComponent(host);
		if (origin is not null)
		{
			http.Request.Headers.Origin = origin;
		}

		Assert.Equal(allowed, PyxieMcpHosting.IsLocal(http));
	}
}
