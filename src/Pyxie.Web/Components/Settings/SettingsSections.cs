using Pyxie.Data.Transfer;

namespace Pyxie.Web.Components.Settings;

/// <summary>The sections of the Settings page in the order they are shown, and which one a URL points at.</summary>
public static class SettingsSections
{
	/// <summary>Gets the agents section, which is the one shown as current until the URL names another.</summary>
	public static SettingsSection Agents { get; } = new("agents", "Agents");

	/// <summary>Gets the gates section.</summary>
	public static SettingsSection Gates { get; } = new("gates", "Gates");

	/// <summary>Gets the categories section.</summary>
	public static SettingsSection Categories { get; } = new("categories", "Categories");

	/// <summary>Gets the sandbox section.</summary>
	public static SettingsSection Sandbox { get; } = new("sandbox", "Sandbox");

	/// <summary>Gets the global settings, always available.</summary>
	public static SettingsSection Global { get; } = new("global", "Global");

	/// <summary>Gets the sections of an open project's settings in display order.</summary>
	public static IReadOnlyList<SettingsSection> ProjectSections { get; } = [Agents, Gates, Categories, Sandbox];

	/// <summary>Gets every settings section in display order.</summary>
	public static IReadOnlyList<SettingsSection> All { get; } = [.. ProjectSections, Global];

	/// <summary>Gets the section that shows a part of the settings a settings file carries, so an import names the parts as the page does.</summary>
	/// <param name="section">The part of the settings.</param>
	/// <returns>The section.</returns>
	public static SettingsSection Of(TransferSection section)
	{
		return section switch
		{
			TransferSection.Agents => Agents,
			TransferSection.Gates => Gates,
			TransferSection.Categories => Categories,
			_ => Sandbox,
		};
	}

	/// <summary>Finds the section a URL points at by its fragment.</summary>
	/// <param name="uri">The absolute URL of the page, such as <c>http://localhost/settings#gates</c>.</param>
	/// <returns>The section the fragment names, or <see cref="Agents"/> when there is no fragment or it names no section.</returns>
	public static SettingsSection FromUri(string uri)
	{
		int hash = uri.IndexOf('#', StringComparison.Ordinal);
		if (hash < 0)
		{
			return Agents;
		}

		string fragment = Uri.UnescapeDataString(uri[(hash + 1)..]);

		return All.FirstOrDefault(section => string.Equals(section.Id, fragment, StringComparison.OrdinalIgnoreCase)) ?? Agents;
	}
}
