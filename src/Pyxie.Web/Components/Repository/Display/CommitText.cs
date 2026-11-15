using System.Globalization;

namespace Pyxie.Web.Components.Repository.Display;

/// <summary>Shapes commit hashes and messages for display.</summary>
public static class CommitText
{
	/// <summary>Shortens a hash the way the commit itself was shortened, so every hash on screen has the same length.</summary>
	/// <param name="sha">The full hash.</param>
	/// <param name="length">The length to keep.</param>
	/// <returns>The first <paramref name="length"/> characters, or the whole hash when it is shorter.</returns>
	public static string Abbreviate(string sha, int length)
	{
		return sha[..Math.Min(length, sha.Length)];
	}

	/// <summary>Gets the letter that stands for an author on the avatar of a commit.</summary>
	/// <param name="authorName">The author's name.</param>
	/// <returns>The first character of the name, upper case; a question mark when the name is empty.</returns>
	public static string Initial(string authorName)
	{
		string trimmed = authorName.Trim();

		return trimmed.Length == 0 ? "?" : StringInfo.GetNextTextElement(trimmed).ToUpperInvariant();
	}

	/// <summary>Splits a commit message into its subject (the first line) and its body (the rest, without the blank line that separates them).</summary>
	/// <param name="message">The full message.</param>
	/// <returns>The subject and the body; the body is empty for a one-line message.</returns>
	public static (string Subject, string Body) Split(string message)
	{
		string[] lines = message.ReplaceLineEndings("\n").Split('\n', 2);
		string body = lines.Length > 1 ? lines[1].Trim('\n') : string.Empty;

		return (lines[0].TrimEnd(), body.TrimEnd());
	}
}
