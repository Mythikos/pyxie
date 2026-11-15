using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>The addresses of the machine Pyxie runs on, which a sandbox's firewall refuses, so no agent reaches Pyxie or anything else on the machine, however it is bound. Loopback addresses are left out, because inside a sandbox they are the sandbox's own, and so are link-local IPv6 addresses, which only mean something with an interface.</summary>
public static class HostAddresses
{
	/// <summary>Gets the machine's addresses, as text.</summary>
	/// <returns>The IPv4 and IPv6 addresses of every interface that is up.</returns>
	public static IReadOnlyList<string> Get()
	{
		SortedSet<string> addresses = new(StringComparer.Ordinal);

		foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (network.OperationalStatus != OperationalStatus.Up)
			{
				continue;
			}

			foreach (UnicastIPAddressInformation unicast in network.GetIPProperties().UnicastAddresses)
			{
				IPAddress address = unicast.Address;
				if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
				{
					continue;
				}

				addresses.Add(new IPAddress(address.GetAddressBytes()).ToString());
			}
		}

		return [.. addresses];
	}
}
