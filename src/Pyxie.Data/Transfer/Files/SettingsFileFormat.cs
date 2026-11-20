namespace Pyxie.Data.Transfer.Files;

/// <summary>Owns the settings file's contract: a JSON document marked with <see cref="Name"/> and a <see cref="CurrentVersion"/>, holding a project's settings with no database ids and no numeric enum values, so it outlives any database. The shape and the vocabulary of each version are frozen: a change to what a file holds or how it is encoded, such as a field, a section, or a harness name, makes a new version, and every older version keeps importing. A file is refused when its version is newer than this one, or when it holds anything its version does not name. The rules an editor applies, such as lengths and tokens, are not part of the contract: they are checked when the file is imported, section by section (docs/settings-file.md).</summary>
public static class SettingsFileFormat
{
	/// <summary>The value of the file's <c>format</c> member, which tells a settings file from any other JSON.</summary>
	public const string Name = "pyxie.project-settings";

	/// <summary>The file version this Pyxie writes, and the newest it reads.</summary>
	public const int CurrentVersion = 3;

	/// <summary>The largest file read, in bytes: far more than any project's settings take.</summary>
	public const int MaxBytes = 1_048_576;

	/// <summary>The media type a settings file is served and picked as.</summary>
	public const string MediaType = "application/json";

	/// <summary>What a settings file's name ends with, after the project's name.</summary>
	public const string FileNameSuffix = ".pyxie-settings.json";

	/// <summary>The file types a browser's file picker offers for a settings file.</summary>
	public const string Accept = ".json,application/json";

	/// <summary>The message for a file larger than <see cref="MaxBytes"/>.</summary>
	public const string TooLargeMessage = "The file is larger than 1 MB, so it is not a Pyxie settings file.";
}
