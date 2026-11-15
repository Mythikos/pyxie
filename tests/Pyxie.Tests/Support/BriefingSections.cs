namespace Pyxie.Tests.Support;

/// <summary>Reads one section of a briefing, so a test asserts what a section says rather than what the briefing contains anywhere.</summary>
public static class BriefingSections
{
	/// <summary>The target branch briefing tests build with, the same as the branch <see cref="TestData.ConnectRepository"/> connects.</summary>
	public const string TargetBranch = "main";

	/// <summary>A note of about 500 characters over several lines, longer than the history line shows.</summary>
	public static string LongMultiLineNote { get; } = string.Join('\n', Enumerable.Range(1, 8).Select(line => $"Finding {line}: the password check ignores the plus sign in case {line}, so sign-in fails."));

	/// <summary>Gets the text of a section: from its heading to the next heading, or the end.</summary>
	/// <param name="briefing">The briefing.</param>
	/// <param name="heading">The heading text without the leading hashes.</param>
	/// <returns>The section, including its heading line.</returns>
	public static string Get(string briefing, string heading)
	{
		string marker = $"## {heading}";
		int start = briefing.IndexOf(marker, StringComparison.Ordinal);
		if (start < 0)
		{
			throw new InvalidOperationException($"The briefing has no '{heading}' section.");
		}

		int next = briefing.IndexOf("\n## ", start + marker.Length, StringComparison.Ordinal);

		return next < 0 ? briefing[start..] : briefing[start..next];
	}
}
