using System.Net;
using System.Net.Sockets;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>A loopback server that accepts connections and never answers, so a <c>git clone</c> of its URL is running (waiting for the reply) until the test ends it, either by closing the connections, which fails the clone, or by cancelling it. It gives a test a clone that is deterministically still in progress.</summary>
public sealed class UnresponsiveServer : IAsyncDisposable
{
	private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
	private readonly List<TcpClient> _connections = [];
	private readonly TaskCompletionSource _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Task _accepting;
	private readonly Lock _gate = new();

	/// <summary>Initializes a new instance of the <see cref="UnresponsiveServer"/> class and starts listening.</summary>
	public UnresponsiveServer()
	{
		_listener.Start();
		_accepting = AcceptAsync();
	}

	/// <summary>Gets the URL a clone of this server waits on.</summary>
	public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/never-answers.git";

	/// <summary>Waits until a client has connected, which for a clone means git is running and waiting.</summary>
	/// <param name="cancellationToken">A token to cancel the wait.</param>
	/// <returns>A task that completes when the first client connected.</returns>
	public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
	{
		await _connected.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
	}

	/// <summary>Closes every connection, so a clone that waits on this server fails.</summary>
	public void CloseConnections()
	{
		lock (_gate)
		{
			foreach (TcpClient connection in _connections)
			{
				connection.Dispose();
			}

			_connections.Clear();
		}
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		_listener.Stop();
		CloseConnections();
		await _accepting.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private async Task AcceptAsync()
	{
		try
		{
			while (true)
			{
				TcpClient client = await _listener.AcceptTcpClientAsync();
				lock (_gate)
				{
					_connections.Add(client);
				}

				_connected.TrySetResult();
			}
		}
		catch (Exception exception) when (exception is ObjectDisposedException or SocketException or InvalidOperationException)
		{
			// The listener was stopped: the test is over.
		}
	}
}
