using System.Reflection;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Counts the handlers attached to an event, so a test can show that a component that listened to a long-lived service stopped listening when it was disposed.</summary>
internal static class EventSubscribers
{
	/// <summary>Counts the handlers of an event declared as a field-like event.</summary>
	/// <param name="owner">The object that raises the event.</param>
	/// <param name="eventName">The name of the event.</param>
	/// <returns>How many handlers are attached.</returns>
	public static int Count(object owner, string eventName)
	{
		FieldInfo field = owner.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
			?? throw new MissingFieldException(owner.GetType().Name, eventName);

		return (field.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;
	}
}
