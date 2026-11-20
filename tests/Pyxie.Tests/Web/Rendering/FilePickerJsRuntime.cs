using Microsoft.JSInterop;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The script runtime of a browser, as far as a file input's picker goes: it counts the times the page asks it to open the picker (Blazorise's <c>showPicker</c>), and can refuse each one with a <see cref="JSException"/>, as a browser does when the request does not come from the person's own press. Every other call does nothing, and importing a script module yields this runtime again, as the module whose functions behave the same way.</summary>
internal sealed class FilePickerJsRuntime : IJSRuntime, IJSObjectReference
{
	/// <summary>The function of Blazorise's utilities module that opens a file input's picker.</summary>
	public const string ShowPickerIdentifier = "showPicker";

	private readonly bool _refuses;
	private int _pickerRequests;

	/// <summary>Initializes a new instance of the <see cref="FilePickerJsRuntime"/> class.</summary>
	/// <param name="refuses">Whether the browser refuses to open the picker.</param>
	public FilePickerJsRuntime(bool refuses = false)
	{
		_refuses = refuses;
	}

	/// <summary>Gets how many times the page asked to open the picker.</summary>
	public int PickerRequests => Volatile.Read(ref _pickerRequests);

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		if (typeof(TValue) == typeof(IJSObjectReference))
		{
			return new ValueTask<TValue>((TValue)(object)this);
		}

		if (identifier != ShowPickerIdentifier)
		{
			return default;
		}

		Interlocked.Increment(ref _pickerRequests);

		return _refuses ? ValueTask.FromException<TValue>(new JSException("File chooser dialog can only be shown with a user activation.")) : default;
	}

	/// <inheritdoc/>
	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
	{
		return InvokeAsync<TValue>(identifier, args);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}
}
