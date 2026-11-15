using System.Net;
using System.Net.Sockets;

using Pyxie.Runtime.Sandboxes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="HostAddresses"/>: the machine's addresses a sandbox's firewall refuses. Loopback is the sandbox's own inside it and an IPv6 link-local address means nothing without an interface, so neither is passed; everything passed must be an address the firewall script can use as it is. A machine may have no other address, so the list may be empty.</summary>
public sealed class HostAddressesTests
{
	[Fact]
	public void Get_EachAddress_ParsesBackAsTheSameIpAddress()
	{
		foreach (string text in HostAddresses.Get())
		{
			Assert.True(IPAddress.TryParse(text, out IPAddress? address), $"{text} is not an IP address.");
			Assert.Equal(text, address.ToString());
			Assert.True(address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6, $"{text} is neither IPv4 nor IPv6.");
			Assert.False(text.Contains('%', StringComparison.Ordinal), $"{text} carries an interface scope, which the firewall cannot use.");
		}
	}

	[Fact]
	public void Get_NoAddress_IsLoopbackOrIPv6LinkLocal()
	{
		foreach (string text in HostAddresses.Get())
		{
			IPAddress address = IPAddress.Parse(text);

			Assert.False(IPAddress.IsLoopback(address), $"{text} is a loopback address, which inside a sandbox is the sandbox's own.");
			Assert.False(address.IsIPv6LinkLocal, $"{text} is an IPv6 link-local address, which means nothing without an interface.");
		}
	}

	[Fact]
	public void Get_Addresses_AreEachListedOnce()
	{
		IReadOnlyList<string> addresses = HostAddresses.Get();

		Assert.Equal(addresses.Count, addresses.Distinct(StringComparer.Ordinal).Count());
	}
}
