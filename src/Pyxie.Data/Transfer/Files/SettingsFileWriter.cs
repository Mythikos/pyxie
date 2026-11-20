namespace Pyxie.Data.Transfer.Files;

/// <summary>Writes settings as a settings file of the current version (<see cref="SettingsFileFormat.CurrentVersion"/>).</summary>
public static class SettingsFileWriter
{
	/// <summary>Writes settings as a settings file: UTF-8 without a byte order mark, LF line breaks, a two-space indent, and a final line break, the same on every machine.</summary>
	/// <param name="settings">The settings; a section that is <see langword="null"/> is left out.</param>
	/// <returns>The file's content.</returns>
	public static byte[] Write(ProjectSettings settings)
	{
		return SettingsFileCodec.Write(settings);
	}
}
