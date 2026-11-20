using Pyxie.Data.Transfer;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>Compares <see cref="ProjectSettings"/> section by section. The record holds its sections as lists, which its own equality compares by reference, so two settings read from the same file are never equal as records.</summary>
public static class SettingsFileAssert
{
	/// <summary>Asserts that two settings hold the same sections with the same items in the same order.</summary>
	/// <param name="expected">The expected settings.</param>
	/// <param name="actual">The settings read or written.</param>
	public static void Same(ProjectSettings expected, ProjectSettings actual)
	{
		Assert.Equal(expected.Sections, actual.Sections);
		SameSection(expected.Agents, actual.Agents, "agents");
		SameSection(expected.Gates, actual.Gates, "gates");
		SameSection(expected.Categories, actual.Categories, "categories");
		Assert.Equal(expected.Sandbox, actual.Sandbox);
	}

	private static void SameSection<TItem>(IReadOnlyList<TItem>? expected, IReadOnlyList<TItem>? actual, string section)
	{
		if (expected is null)
		{
			Assert.True(actual is null, $"The settings should not hold {section}, but hold {actual?.Count} of them.");

			return;
		}

		Assert.True(actual is not null, $"The settings should hold {section}, but do not.");
		Assert.Equal(expected, actual);
	}
}
