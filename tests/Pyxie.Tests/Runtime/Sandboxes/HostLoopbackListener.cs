using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>A tiny HTTP service bound to the host's <c>127.0.0.1</c>, standing in for Pyxie, which has no sign-in: every request gets <see cref="Reply"/>. Docker Desktop lets a container reach services bound to the host's loopback through <c>host.docker.internal</c>, which is what a sandbox's firewall must refuse. Disposing it stops it.</summary>
public sealed class HostLoopbackListener : IAsyncDisposable
{
	/// <summary>The body every request gets back.</summary>
	public const string Reply = "pyxie-host-service";

	private readonly TcpListener _listener;
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _serving;

	/// <summary>Initializes a new instance of the <see cref="HostLoopbackListener"/> class and starts serving on a free port.</summary>
	public HostLoopbackListener()
	{
		_listener = new TcpListener(IPAddress.Loopback, 0);
		_listener.Start();
		Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
		_serving = ServeAsync(_stop.Token);
	}

	/// <summary>Gets the port the service listens on.</summary>
	public int Port { get; }

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _stop.CancelAsync();
		_listener.Stop();
		await _serving;
		_stop.Dispose();
	}

	private async Task ServeAsync(CancellationToken cancellationToken)
	{
		byte[] response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: {Reply.Length}\r\nConnection: close\r\n\r\n{Reply}");
		byte[] request = new byte[8192];

		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await _listener.AcceptTcpClientAsync(cancellationToken);
			}
			catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
			{
				return;
			}

			using (client)
			{
				try
				{
					NetworkStream stream = client.GetStream();
					if (await stream.ReadAsync(request, cancellationToken) > 0)
					{
						await stream.WriteAsync(response, cancellationToken);
					}
				}
				catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException)
				{
					// A client that went away before its answer needs none.
				}
			}
		}
	}
}
