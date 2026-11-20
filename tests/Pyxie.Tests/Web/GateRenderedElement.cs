namespace Pyxie.Tests.Web;

/// <summary>An element in the tree a <see cref="GateInteractiveHost"/> currently shows: what a person would see and which events it listens to.</summary>
/// <param name="Name">The element name, such as <c>button</c>.</param>
/// <param name="Attributes">The attributes with a text or flag value, by name.</param>
/// <param name="EventHandlers">The event handler identifiers by event name, such as <c>onclick</c>.</param>
/// <param name="Text">The text of the element and everything inside it, concatenated.</param>
/// <param name="Key">The <c>@key</c> the markup gave the element, or <see langword="null"/> when it has none. An element whose key changed is a new element in the browser, not the old one changed.</param>
/// <param name="ReferenceId">The identifier of the element reference the component captured for the element with <c>@ref</c>, or <see langword="null"/> when it captured none. It is the <c>Id</c> of the reference a component passes to the browser, such as for focusing the element.</param>
/// <param name="BubblingClickHandlers">The click handlers of the elements around this one that a click on it reaches as it bubbles up, nearest first, up to an element that stops it; <see langword="null"/> for none.</param>
public sealed record GateRenderedElement(string Name, IReadOnlyDictionary<string, string> Attributes, IReadOnlyDictionary<string, ulong> EventHandlers, string Text, object? Key = null, string? ReferenceId = null, IReadOnlyList<ulong>? BubblingClickHandlers = null)
{
	/// <summary>Gets the value of an attribute, or <see langword="null"/> when the element has none.</summary>
	/// <param name="attribute">The attribute name.</param>
	/// <returns>The attribute value.</returns>
	public string? Attribute(string attribute) => Attributes.GetValueOrDefault(attribute);

	/// <summary>Gets a value indicating whether a person could press the element: a click on it reaches a handler, its own or one around it, and it is not disabled.</summary>
	public bool IsClickable => (EventHandlers.ContainsKey("onclick") || BubblingClickHandlers is [_, ..]) && !Attributes.ContainsKey("disabled");
}
