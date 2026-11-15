using Microsoft.JSInterop;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The JavaScript runtime of a component under test that is allowed to call the browser: it records every call, answers each with nothing, and can be told to refuse them the way a browser without the feature does.</summary>
internal sealed class RecordingJSRuntime : IJSRuntime
{
	private readonly List<(string Identifier, object?[] Arguments)> _calls = [];

	/// <summary>Gets or sets the failure every call throws, or <see langword="null"/> to let calls succeed.</summary>
	public Exception? Failure { get; set; }

	/// <summary>Gets the calls made so far, in order.</summary>
	public IReadOnlyList<(string Identifier, object?[] Arguments)> Calls => _calls;

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		_calls.Add((identifier, args ?? []));

		return Failure is null ? default : ValueTask.FromException<TValue>(Failure);
	}

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
	{
		return InvokeAsync<TValue>(identifier, args);
	}
}
