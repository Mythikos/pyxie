namespace Pyxie.Web.Components.Settings;

/// <summary>One section of the Settings page: the anchor its panel answers to and the name the section nav shows.</summary>
/// <param name="Id">The anchor, which is also the <c>id</c> of the section's panel.</param>
/// <param name="Title">The name of the section, which the nav and the panel's heading show.</param>
public sealed record SettingsSection(string Id, string Title);
