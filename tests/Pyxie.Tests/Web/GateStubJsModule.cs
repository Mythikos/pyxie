using Microsoft.JSInterop;

namespace Pyxie.Tests.Web;

/// <summary>A script module whose functions do nothing and return nothing.</summary>
public sealed class GateStubJsModule : IJSObjectReference
{
	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;

	/// <inheritdoc/>
	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
