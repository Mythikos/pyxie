using System.Runtime.ExceptionServices;
using System.Text;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Xunit;

// A renderer cannot be written without the render tree types: it must override UpdateDisplayAsync, and finding what a person can press means reading the frames. The analyzer's warning is about application code depending on types that may change, which is what a test host of the framework does on purpose, as bUnit does.
#pragma warning disable BL0006

namespace Pyxie.Tests.Web;

/// <summary>Renders a component without a browser and lets a test press its buttons and type into its fields, the way a person would, by raising the events the rendered elements listen to. It reads the component tree as it is after each interaction, so a test asserts what a person would see and what reached the database.</summary>
public sealed class GateInteractiveHost : Renderer
{
	// What @onclick:stopPropagation renders on an element: a click inside it goes no further up.
	private const string StopClickPropagation = "__internal_stopPropagation_onclick";

	private readonly List<Exception> _failures = [];
	private readonly List<TaskCompletionSource> _unacknowledged = [];
	private int _rootComponentId = -1;

	// Read and written only on the dispatcher, where renders happen.
	private bool _holdsAcknowledgements;

	/// <summary>Initializes a new instance of the <see cref="GateInteractiveHost"/> class.</summary>
	/// <param name="services">The services components are created with.</param>
	public GateInteractiveHost(IServiceProvider services)
		: base(services, NullLoggerFactory.Instance)
	{
		Dispatcher = Dispatcher.CreateDefault();

		// A component that focuses an element it captured with @ref reaches the browser through the services' script runtime, as it does in the application.
		if (services.GetService(typeof(IJSRuntime)) is IJSRuntime jsRuntime)
		{
			ElementReferenceContext = new WebElementReferenceContext(jsRuntime);
		}
	}

	/// <inheritdoc/>
	public override Dispatcher Dispatcher { get; }

	/// <summary>Gets what the components are told about where they render: interactively on the server, as the application's pages do once the circuit is up, after prerendering.</summary>
	protected override RendererInfo RendererInfo { get; } = new("Server", isInteractive: true);

	/// <summary>Renders a component as the root of the tree and waits until it has finished loading.</summary>
	/// <typeparam name="TComponent">The component to render.</typeparam>
	/// <param name="parameters">The component parameters by name, or <see langword="null"/> for none.</param>
	/// <returns>A task that completes when the component has rendered.</returns>
	public async Task StartAsync<TComponent>(IReadOnlyDictionary<string, object?>? parameters = null)
		where TComponent : IComponent
	{
		ParameterView view = ParameterView.FromDictionary(parameters is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(parameters));

		await Dispatcher.InvokeAsync(async () =>
		{
			IComponent component = InstantiateComponent(typeof(TComponent));
			_rootComponentId = AssignRootComponentId(component);
			await RenderRootComponentAsync(_rootComponentId, view);
		});

		ThrowIfFailed();
	}

	/// <summary>Gives the root component new parameters, as its parent rendering again would, and waits until it has rendered.</summary>
	/// <param name="parameters">The component parameters by name.</param>
	/// <returns>A task that completes when the component has rendered.</returns>
	public async Task UpdateAsync(IReadOnlyDictionary<string, object?> parameters)
	{
		ParameterView view = ParameterView.FromDictionary(new Dictionary<string, object?>(parameters));

		await Dispatcher.InvokeAsync(() => RenderRootComponentAsync(_rootComponentId, view));

		ThrowIfFailed();
	}

	/// <summary>Disposes every component in the tree, as leaving the page does. The host stays safe to dispose afterwards.</summary>
	/// <returns>A task that completes when the components have been disposed.</returns>
	public ValueTask CloseAsync()
	{
		return DisposeAsync();
	}

	/// <summary>Reads every element in the tree as it is now, in document order.</summary>
	/// <returns>The elements.</returns>
	public async Task<IReadOnlyList<GateRenderedElement>> ElementsAsync()
	{
		return await Dispatcher.InvokeAsync(() =>
		{
			List<GateRenderedElement> elements = [];
			Collect(_rootComponentId, elements, []);

			return (IReadOnlyList<GateRenderedElement>)elements;
		});
	}

	/// <summary>Finds the one button with an accessible name: its <c>aria-label</c>, or its text when it has none.</summary>
	/// <param name="name">The accessible name.</param>
	/// <returns>The button.</returns>
	public async Task<GateRenderedElement> FindButtonAsync(string name)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<GateRenderedElement> matches = elements.Where(element => element.Name == "button" && (element.Attribute("aria-label") ?? element.Text) == name).ToList();

		return Assert.Single(matches);
	}

	/// <summary>Finds the one text field of a form model property, such as <c>_model.Name</c>.</summary>
	/// <param name="property">The bound property, as the markup binds it.</param>
	/// <returns>The field.</returns>
	public async Task<GateRenderedElement> FindFieldAsync(string property)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<GateRenderedElement> matches = elements.Where(element => element.Name == "input" && element.Attribute("name") == property).ToList();

		return Assert.Single(matches);
	}

	/// <summary>Finds the one option of a select by its text. The options of a closed select stay in the tree, hidden.</summary>
	/// <param name="text">The option text.</param>
	/// <returns>The option.</returns>
	public async Task<GateRenderedElement> FindOptionAsync(string text)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<GateRenderedElement> matches = elements.Where(element => (element.Attribute("class") ?? string.Empty).Split(' ').Contains("ant-select-item-option") && element.Text == text).ToList();

		return Assert.Single(matches);
	}

	/// <summary>Finds the one switch of a form model property, such as <c>_model.IsEnabled</c>.</summary>
	/// <param name="property">The bound property, as the markup binds it.</param>
	/// <returns>The switch.</returns>
	public async Task<GateRenderedElement> FindSwitchAsync(string property)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<GateRenderedElement> matches = elements.Where(element => element.Name == "button" && element.Attribute("role") == "switch" && element.Attribute("name") == property).ToList();

		return Assert.Single(matches);
	}

	/// <summary>Finds the one switch with an accessible name, such as <c>Enable Build</c>.</summary>
	/// <param name="name">The <c>aria-label</c> of the switch.</param>
	/// <returns>The switch.</returns>
	public async Task<GateRenderedElement> FindLabelledSwitchAsync(string name)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<GateRenderedElement> matches = elements.Where(element => element.Name == "button" && element.Attribute("role") == "switch" && element.Attribute("aria-label") == name).ToList();

		return Assert.Single(matches);
	}

	/// <summary>Finds the one checkbox with a label, such as <c>Can work</c>. A checkbox has no name of its own, so it is found by the label it sits in.</summary>
	/// <param name="label">The label text.</param>
	/// <returns>The checkbox input; its <c>checked</c> attribute is <c>true</c> while it is ticked.</returns>
	public async Task<GateRenderedElement> FindCheckboxAsync(string label)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		List<int> labels = [.. Enumerable.Range(0, elements.Count).Where(index => (elements[index].Attribute("class") ?? string.Empty).Split(' ').Contains("ant-checkbox-wrapper") && elements[index].Text.Trim() == label)];
		int wrapper = Assert.Single(labels);

		return elements.Skip(wrapper + 1).First(element => element.Name == "input" && element.Attribute("type") == "checkbox");
	}

	/// <summary>Finds the one checkbox with an accessible name: its <c>aria-label</c>.</summary>
	/// <param name="name">The <c>aria-label</c> of the checkbox.</param>
	/// <returns>The checkbox input; its <c>checked</c> attribute is <c>true</c> while it is ticked.</returns>
	public async Task<GateRenderedElement> FindNamedCheckboxAsync(string name)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();

		return Assert.Single(elements, element => element.Name == "input" && element.Attribute("type") == "checkbox" && element.Attribute("aria-label") == name);
	}

	/// <summary>Finds the one radio button with an accessible name: its <c>aria-label</c>.</summary>
	/// <param name="name">The <c>aria-label</c> of the radio button.</param>
	/// <returns>The radio input; its <c>checked</c> attribute is <c>true</c> while it is chosen.</returns>
	public async Task<GateRenderedElement> FindRadioAsync(string name)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();

		return Assert.Single(elements, element => element.Name == "input" && element.Attribute("type") == "radio" && element.Attribute("aria-label") == name);
	}

	/// <summary>Finds every element with a name that satisfies a condition, in document order.</summary>
	/// <param name="name">The element name, such as <c>li</c>.</param>
	/// <param name="condition">The condition the element has to satisfy, or <see langword="null"/> for every element of that name.</param>
	/// <returns>The elements.</returns>
	public async Task<IReadOnlyList<GateRenderedElement>> FindElementsAsync(string name, Func<GateRenderedElement, bool>? condition = null)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();

		return elements.Where(element => element.Name == name && (condition?.Invoke(element) ?? true)).ToList();
	}

	/// <summary>Finds every component of a type in the tree as it is now, parents before their children, such as the panels of a page, to tell a panel that was made anew from one that only rendered again.</summary>
	/// <typeparam name="TComponent">The component type.</typeparam>
	/// <returns>The component instances.</returns>
	public async Task<IReadOnlyList<TComponent>> FindComponentsAsync<TComponent>()
		where TComponent : IComponent
	{
		return await Dispatcher.InvokeAsync(() =>
		{
			List<TComponent> components = [];
			CollectComponents(_rootComponentId, components);

			return (IReadOnlyList<TComponent>)components;
		});
	}

	/// <summary>Raises what the one component of a type in the tree reports to its parent through one of its <see cref="EventCallback{TValue}"/> parameters, as the component does when its script tells it what the browser did, such as a file picked in a file input or a modal that finished its animation. The host runs no script, so the test raises the report the script would have made the component send.</summary>
	/// <typeparam name="TComponent">The component type, of which the tree holds exactly one.</typeparam>
	/// <typeparam name="TValue">The type of the value the callback reports.</typeparam>
	/// <param name="callback">Picks the callback parameter from the component, such as its <c>Changed</c>.</param>
	/// <param name="value">The value the component reports.</param>
	/// <returns>A task that completes when the parent has handled the report and rendered.</returns>
	public async Task RaiseComponentEventAsync<TComponent, TValue>(Func<TComponent, EventCallback<TValue>> callback, TValue value)
		where TComponent : IComponent
	{
		TComponent component = Assert.Single(await FindComponentsAsync<TComponent>());

		await Dispatcher.InvokeAsync(() => callback(component).InvokeAsync(value));

		ThrowIfFailed();
	}

	/// <summary>Ticks or clears a checkbox, as clicking it does.</summary>
	/// <param name="element">The checkbox, read from this host after the last interaction.</param>
	/// <param name="isChecked">Whether the checkbox is ticked afterwards.</param>
	/// <returns>A task that completes when the component has handled the change and rendered.</returns>
	public async Task SetCheckedAsync(GateRenderedElement element, bool isChecked)
	{
		Assert.True(element.EventHandlers.TryGetValue("onchange", out ulong handler), $"The <{element.Name}> has no change handler.");

		await RaiseAsync(handler, new ChangeEventArgs { Value = isChecked });
	}

	/// <summary>Reads the text of every alert shown.</summary>
	/// <returns>The alert messages, in document order.</returns>
	public async Task<IReadOnlyList<string>> AlertsAsync()
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();

		return elements.Where(element => element.Attribute("role") == "alert").Select(element => element.Text).ToList();
	}

	/// <summary>Determines whether the modal is open. A closed modal stays in the tree, hidden.</summary>
	/// <returns><see langword="true"/> when a person can see the modal.</returns>
	public async Task<bool> IsModalOpenAsync()
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();
		GateRenderedElement wrap = Assert.Single(elements, element => (element.Attribute("class") ?? string.Empty).Split(' ').Contains("ant-modal-wrap"));

		return !(wrap.Attribute("style") ?? string.Empty).Contains("display: none", StringComparison.Ordinal);
	}

	/// <summary>Reads the text of every element that has a class.</summary>
	/// <param name="cssClass">The class to look for.</param>
	/// <returns>The text of each element, in document order.</returns>
	public async Task<IReadOnlyList<string>> TextsWithClassAsync(string cssClass)
	{
		IReadOnlyList<GateRenderedElement> elements = await ElementsAsync();

		return elements.Where(element => (element.Attribute("class") ?? string.Empty).Split(' ').Contains(cssClass)).Select(element => element.Text).ToList();
	}

	/// <summary>Presses an element a person could press. The click reaches the element's own handler and then, as it bubbles up, the handlers around it, as in the browser, which sends the click to each of them before it hears what any of them did.</summary>
	/// <param name="element">The element, read from this host after the last interaction.</param>
	/// <param name="clicks">The click count the browser reports: 1 for a click, 2 for the second click of a double click.</param>
	/// <returns>A task that completes when the components have handled the press and rendered.</returns>
	public async Task ClickAsync(GateRenderedElement element, long clicks = 1)
	{
		Assert.True(element.IsClickable, $"The <{element.Name}> '{element.Attribute("aria-label") ?? element.Text}' cannot be pressed: no click handler reaches it, or it is disabled.");

		List<ulong> handlers = [.. element.EventHandlers.TryGetValue("onclick", out ulong own) ? [own] : Array.Empty<ulong>(), .. element.BubblingClickHandlers ?? []];
		MouseEventArgs press = new() { Detail = clicks };

		if (handlers.Count == 1)
		{
			await RaiseAsync(handlers[0], press);

			return;
		}

		// Every handler the click reaches gets it before the browser applies a render the first one caused, so the renders wait, unless a test already holds them.
		bool holds = await Dispatcher.InvokeAsync(() => _holdsAcknowledgements);
		await HoldAcknowledgementsAsync();

		try
		{
			foreach (ulong handler in handlers)
			{
				await RaiseAsync(handler, press);
			}
		}
		finally
		{
			if (!holds)
			{
				await AcknowledgeAsync();
			}
		}
	}

	/// <summary>Submits a form, as pressing its submit button or Enter in one of its fields does.</summary>
	/// <param name="form">The form element, read from this host after the last interaction.</param>
	/// <returns>A task that completes when the component has handled the submission and rendered.</returns>
	public async Task SubmitAsync(GateRenderedElement form)
	{
		Assert.True(form.Name == "form" && form.EventHandlers.TryGetValue("onsubmit", out ulong handler), $"The <{form.Name}> is not a form that handles submission.");

		await RaiseAsync(form.EventHandlers["onsubmit"], new EventArgs());
	}

	/// <summary>Types a value into a text field.</summary>
	/// <param name="element">The field, read from this host after the last interaction.</param>
	/// <param name="value">The new text.</param>
	/// <returns>A task that completes when the component has handled the input and rendered.</returns>
	public async Task TypeAsync(GateRenderedElement element, string value)
	{
		string eventName = element.EventHandlers.ContainsKey("oninput") ? "oninput" : "onchange";
		Assert.True(element.EventHandlers.TryGetValue(eventName, out ulong handler), $"The <{element.Name}> has no input handler.");

		await RaiseAsync(handler, new ChangeEventArgs { Value = value });
	}

	/// <summary>Presses a key while an element has the focus.</summary>
	/// <param name="element">The element, read from this host after the last interaction.</param>
	/// <param name="key">The value of the key, such as <c>ArrowUp</c>.</param>
	/// <returns>A task that completes when the component has handled the key and rendered.</returns>
	public async Task PressKeyAsync(GateRenderedElement element, string key)
	{
		Assert.True(element.EventHandlers.TryGetValue("onkeydown", out ulong handler), $"The <{element.Name}> has no key handler.");

		await RaiseAsync(handler, new KeyboardEventArgs { Key = key, Type = "keydown" });
	}

	/// <summary>Drags one element and drops it on another, as a pointer does: the drag starts on the first, passes over the second, and the drop lands on it. Every interaction renders the component again and the elements it listens on are new, so the target is picked from the tree as it is when each one happens.</summary>
	/// <param name="source">The element that is dragged, read from this host after the last interaction.</param>
	/// <param name="pickTarget">Picks the element the drag is dropped on from the elements the host shows.</param>
	/// <returns>A task that completes when the component has handled the drop and rendered.</returns>
	public async Task DragAsync(GateRenderedElement source, Func<IReadOnlyList<GateRenderedElement>, GateRenderedElement> pickTarget)
	{
		await StartDragAsync(source);
		await DragEnterAsync(pickTarget(await ElementsAsync()));
		await DropOnAsync(pickTarget(await ElementsAsync()));
	}

	/// <summary>Starts dragging an element, as a pointer pressed on it and moved does.</summary>
	/// <param name="source">The element that is dragged, read from this host after the last interaction.</param>
	/// <returns>A task that completes when the component has handled the start and rendered.</returns>
	public async Task StartDragAsync(GateRenderedElement source)
	{
		Assert.True(source.Attribute("draggable") == "true", $"The <{source.Name}> is not draggable.");
		Assert.True(source.EventHandlers.TryGetValue("ondragstart", out ulong start), $"The <{source.Name}> has no drag handler.");

		await RaiseAsync(start, new DragEventArgs { Type = "dragstart" });
	}

	/// <summary>Moves a drag that is in progress onto an element.</summary>
	/// <param name="target">The element the drag enters, read from this host after the last interaction.</param>
	/// <returns>A task that completes when the component has handled it and rendered.</returns>
	public async Task DragEnterAsync(GateRenderedElement target)
	{
		Assert.True(target.EventHandlers.TryGetValue("ondragenter", out ulong enter), $"The <{target.Name}> has no handler for a drag entering it.");

		await RaiseAsync(enter, new DragEventArgs { Type = "dragenter" });
	}

	/// <summary>Drops what is dragged on an element.</summary>
	/// <param name="target">The element the drop lands on, read from this host after the last interaction.</param>
	/// <returns>A task that completes when the component has handled the drop and rendered.</returns>
	public async Task DropOnAsync(GateRenderedElement target)
	{
		Assert.True(target.EventHandlers.TryGetValue("ondrop", out ulong drop), $"The <{target.Name}> has no drop handler.");

		await RaiseAsync(drop, new DragEventArgs { Type = "drop" });
	}

	/// <summary>Ends a drag, as the browser does after the drop or when the drag was let go anywhere else.</summary>
	/// <param name="source">The element that was dragged, read from this host after the last interaction.</param>
	/// <returns>A task that completes when the component has handled it and rendered.</returns>
	public async Task EndDragAsync(GateRenderedElement source)
	{
		Assert.True(source.EventHandlers.TryGetValue("ondragend", out ulong end), $"The <{source.Name}> has no handler for a drag ending.");

		await RaiseAsync(end, new DragEventArgs { Type = "dragend" });
	}

	/// <summary>Has the browser fall behind: from now on each render waits for the browser to acknowledge it, until <see cref="AcknowledgeAsync"/>. In the application a render is done once the browser has applied it, and until then the handlers of the elements it removed or replaced keep working, because the browser can still send events to them. A test holds acknowledgements to press an element again as a person does whose second click left before the first one's render arrived; otherwise every render is acknowledged at once.</summary>
	/// <returns>A task that completes when renders wait.</returns>
	public async Task HoldAcknowledgementsAsync()
	{
		await Dispatcher.InvokeAsync(() =>
		{
			_holdsAcknowledgements = true;
		});
	}

	/// <summary>Has the browser catch up: it acknowledges every render it has not acknowledged yet, in order, with the renders those lead to, and from then on acknowledges each render at once. The handlers of the elements the acknowledged renders removed or replaced stop working, and the components hear that they rendered.</summary>
	/// <returns>A task that completes when the renders have been acknowledged.</returns>
	public async Task AcknowledgeAsync()
	{
		await Dispatcher.InvokeAsync(() =>
		{
			// Hearing that it rendered can make a component render again. That render reaches the browser after the ones before it, so it waits for them and is acknowledged in the next round.
			while (_unacknowledged.Count > 0)
			{
				List<TaskCompletionSource> acknowledgements = [.. _unacknowledged];
				_unacknowledged.Clear();

				foreach (TaskCompletionSource acknowledgement in acknowledgements)
				{
					acknowledgement.SetResult();
				}
			}

			_holdsAcknowledgements = false;
		});

		ThrowIfFailed();
	}

	/// <inheritdoc/>
	protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
	{
		if (!_holdsAcknowledgements)
		{
			return Task.CompletedTask;
		}

		TaskCompletionSource acknowledgement = new();
		_unacknowledged.Add(acknowledgement);

		return acknowledgement.Task;
	}

	/// <inheritdoc/>
	protected override void Dispose(bool disposing)
	{
		// Renders the browser never acknowledged are cancelled, as the application's renderer cancels them when the circuit goes away. The base class runs this again on the dispatcher when it is called from elsewhere.
		if (Dispatcher.CheckAccess())
		{
			foreach (TaskCompletionSource acknowledgement in _unacknowledged)
			{
				acknowledgement.TrySetCanceled();
			}

			_unacknowledged.Clear();
		}

		base.Dispose(disposing);
	}

	/// <inheritdoc/>
	protected override void HandleException(Exception exception)
	{
		_failures.Add(exception);
	}

	private async Task RaiseAsync(ulong handler, EventArgs arguments)
	{
		await Dispatcher.InvokeAsync(() => DispatchEventAsync(handler, null, arguments));

		ThrowIfFailed();
	}

	private void ThrowIfFailed()
	{
		if (_failures.Count > 0)
		{
			ExceptionDispatchInfo.Capture(_failures[0]).Throw();
		}
	}

	// A component's frames hold the frames of the components it renders, each followed by its parameters; what those render is in their own frames.
	private void CollectComponents<TComponent>(int componentId, List<TComponent> components)
	{
		ArrayRange<RenderTreeFrame> frames = GetCurrentRenderTreeFrames(componentId);

		for (int index = 0; index < frames.Count; index++)
		{
			RenderTreeFrame frame = frames.Array[index];

			if (frame.FrameType != RenderTreeFrameType.Component)
			{
				continue;
			}

			if (frame.Component is TComponent component)
			{
				components.Add(component);
			}

			CollectComponents(frame.ComponentId, components);
		}
	}

	// Click handlers reach the elements inside them, so each element is read with the handlers around it, nearest first.
	private void Collect(int componentId, List<GateRenderedElement> elements, IReadOnlyList<ulong> around)
	{
		ArrayRange<RenderTreeFrame> frames = GetCurrentRenderTreeFrames(componentId);
		Collect(frames, 0, frames.Count, elements, around);
	}

	private void Collect(ArrayRange<RenderTreeFrame> frames, int start, int end, List<GateRenderedElement> elements, IReadOnlyList<ulong> around)
	{
		int index = start;

		while (index < end)
		{
			RenderTreeFrame frame = frames.Array[index];

			if (frame.FrameType == RenderTreeFrameType.Component)
			{
				Collect(frame.ComponentId, elements, around);
				index += frame.ComponentSubtreeLength;
			}
			else if (frame.FrameType == RenderTreeFrameType.Element)
			{
				GateRenderedElement element = ReadElement(frames, index, around);
				elements.Add(element);
				Collect(frames, index + 1, index + frame.ElementSubtreeLength, elements, Inside(element, around));
				index += frame.ElementSubtreeLength;
			}
			else
			{
				index++;
			}
		}
	}

	// The handlers a click inside an element reaches after its own: the element's, then those around it, unless the element stops the click there. The browser sends no click from inside a disabled control.
	private static IReadOnlyList<ulong> Inside(GateRenderedElement element, IReadOnlyList<ulong> around)
	{
		if (element.Name is "button" or "input" or "select" or "textarea" && element.Attributes.ContainsKey("disabled"))
		{
			return [];
		}

		List<ulong> handlers = element.EventHandlers.TryGetValue("onclick", out ulong own) ? [own] : [];

		if (!StopsClicks(element))
		{
			handlers.AddRange(around);
		}

		return handlers;
	}

	private static bool StopsClicks(GateRenderedElement element)
	{
		return element.Attribute(StopClickPropagation) == "true";
	}

	private static GateRenderedElement ReadElement(ArrayRange<RenderTreeFrame> frames, int index, IReadOnlyList<ulong> around)
	{
		RenderTreeFrame element = frames.Array[index];
		int lastAttribute = index + CountAttributes(frames, index);
		Dictionary<string, string> attributes = [];
		Dictionary<string, ulong> handlers = [];
		StringBuilder text = new();
		string? referenceId = null;

		for (int next = index + 1; next < index + element.ElementSubtreeLength; next++)
		{
			RenderTreeFrame frame = frames.Array[next];

			if (frame.FrameType == RenderTreeFrameType.Attribute && next <= lastAttribute)
			{
				if (frame.AttributeEventHandlerId != 0)
				{
					handlers[frame.AttributeName] = frame.AttributeEventHandlerId;
				}
				else if (frame.AttributeValue is bool flag)
				{
					if (flag)
					{
						attributes[frame.AttributeName] = "true";
					}
				}
				else if (frame.AttributeValue is not null)
				{
					attributes[frame.AttributeName] = frame.AttributeValue.ToString() ?? string.Empty;
				}
			}
			else if (frame.FrameType == RenderTreeFrameType.ElementReferenceCapture && next <= lastAttribute)
			{
				referenceId = frame.ElementReferenceCaptureId;
			}
			else if (frame.FrameType == RenderTreeFrameType.Text)
			{
				text.Append(frame.TextContent);
			}
		}

		GateRenderedElement read = new(element.ElementName, attributes, handlers, text.ToString(), element.ElementKey, referenceId);

		return StopsClicks(read) ? read : read with { BubblingClickHandlers = around };
	}

	// An element's attributes, and the reference the component captured for it, are the frames that follow it directly, before its first child.
	private static int CountAttributes(ArrayRange<RenderTreeFrame> frames, int index)
	{
		int count = 0;

		while (index + 1 + count < frames.Count && frames.Array[index + 1 + count].FrameType is RenderTreeFrameType.Attribute or RenderTreeFrameType.ElementReferenceCapture)
		{
			count++;
		}

		return count;
	}
}
