using Pyxie.Data.Transfer;

namespace Pyxie.Web.Components.Settings.Models;

/// <summary>What the import dialog opens with when a settings file was read: its settings, and what importing each of its sections would do to the open project.</summary>
/// <param name="Settings">The file's settings.</param>
/// <param name="Preview">What importing each section would do.</param>
public sealed record SettingsImportOpening(ProjectSettings Settings, SettingsImportPreview Preview);
