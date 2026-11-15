namespace Pyxie.Runtime.Repositories;

/// <summary>Names derived from where a repository was imported from: the name people know it by, and text made safe to use as one part of a file name.</summary>
public static class SourceNames
{
	/// <summary>Gets the name of a repository from the folder or URL it was imported from: the last part, without a <c>.git</c> suffix.</summary>
	/// <param name="origin">The folder or URL.</param>
	/// <returns>The name; the origin itself when it has no parts.</returns>
	public static string NameOf(string origin)
	{
		string trimmed = origin.TrimEnd('/', '\\');
		string last = trimmed.Split(['/', '\\', ':']).LastOrDefault(part => part.Length > 0) ?? trimmed;

		return last.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? last[..^4] : last;
	}

	/// <summary>Makes text usable as one part of a file name: separators and characters no file system takes become dashes. A branch like <c>feature/login</c> becomes <c>feature-login</c>.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The text with every such character replaced.</returns>
	public static string FileNameOf(string text)
	{
		HashSet<char> invalid = [.. Path.GetInvalidFileNameChars(), '/', '\\', ':'];

		return string.Concat(text.Select(character => invalid.Contains(character) ? '-' : character));
	}
}
