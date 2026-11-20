using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Transfer.Planning;

/// <summary>Matches a settings file's items to a project's items by name, ignoring case, so a matched item is updated in place and cards keep referring to it. An item whose name is exactly a stored name takes that item; otherwise it takes the first stored item whose name differs only in case. The stored names' unique indexes compare exactly, so this order means no write can ever collide with one: a matched item only takes a name no other remaining item holds exactly, an added item's name matches no stored name in any case, and the file's names are unique ignoring case (<see cref="SettingsRules"/>).</summary>
internal static class NameMatcher
{
	/// <summary>Matches items by name.</summary>
	/// <typeparam name="TExisting">The kind of item the project stores.</typeparam>
	/// <typeparam name="TIncoming">The kind of item the file holds.</typeparam>
	/// <param name="existing">The project's items, in the order they were made.</param>
	/// <param name="existingName">The name of a project item.</param>
	/// <param name="incoming">The file's items, in the file's order, with names unique ignoring case.</param>
	/// <param name="incomingName">The name of a file item.</param>
	/// <returns>The matched, added, and removed items.</returns>
	public static NamePlan<TExisting, TIncoming> Match<TExisting, TIncoming>(IReadOnlyList<TExisting> existing, Func<TExisting, string> existingName, IReadOnlyList<TIncoming> incoming, Func<TIncoming, string> incomingName)
	{
		bool[] claimed = new bool[existing.Count];
		List<(TExisting Existing, TIncoming Incoming)> matched = [];
		List<TIncoming> added = [];

		foreach (TIncoming item in incoming)
		{
			string name = incomingName(item);
			int match = IndexOf(existing, existingName, claimed, stored => string.Equals(stored, name, StringComparison.Ordinal));
			if (match < 0)
			{
				match = IndexOf(existing, existingName, claimed, stored => InputRules.NameComparer.Equals(stored, name));
			}

			if (match < 0)
			{
				added.Add(item);
				continue;
			}

			claimed[match] = true;
			matched.Add((existing[match], item));
		}

		List<TExisting> removed = [.. existing.Where((_, index) => !claimed[index])];

		return new NamePlan<TExisting, TIncoming>(matched, added, removed);
	}

	private static int IndexOf<TExisting>(IReadOnlyList<TExisting> existing, Func<TExisting, string> existingName, bool[] claimed, Func<string, bool> fits)
	{
		for (int index = 0; index < existing.Count; index++)
		{
			if (!claimed[index] && fits(existingName(existing[index])))
			{
				return index;
			}
		}

		return -1;
	}
}
