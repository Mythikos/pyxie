using System.Collections.Concurrent;

using Microsoft.JSInterop;

namespace Pyxie.Tests.Web;

/// <summary>A JavaScript runtime that does nothing, so Blazorise components can run without a browser, and remembers what it was asked to do, so a test can see what a component asked the browser for. Importing a script module yields a module that does nothing too.</summary>
public sealed class GateStubJsRuntime : IJSRuntime
{
	/// <summary>The function the framework calls to give an element the focus.</summary>
	public const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

	private readonly ConcurrentQueue<(string Identifier, object?[] Arguments)> _calls = new();

	/// <summary>Gets every call made so far, in order.</summary>
	public IReadOnlyList<(string Identifier, object?[] Arguments)> Calls => [.. _calls];

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		_calls.Enqueue((identifier, args ?? []));

		return typeof(TValue) == typeof(IJSObjectReference) ? new ValueTask<TValue>((TValue)(object)new GateStubJsModule()) : default;
	}

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
	{
		return InvokeAsync<TValue>(identifier, args);
	}
}
