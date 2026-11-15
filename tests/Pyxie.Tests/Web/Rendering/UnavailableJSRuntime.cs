using Microsoft.JSInterop;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The JavaScript runtime of a component rendered to HTML on the server: it exists so components can be created, and refuses every call like the one of a static render.</summary>
internal sealed class UnavailableJSRuntime : IJSRuntime
{
	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		throw new InvalidOperationException($"JavaScript interop is not available while rendering to HTML ({identifier}).");
	}

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
	{
		throw new InvalidOperationException($"JavaScript interop is not available while rendering to HTML ({identifier}).");
	}
}
