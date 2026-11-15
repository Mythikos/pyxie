namespace Pyxie.Runtime.Workspaces;

/// <summary>The files in a repository that steer an agent harness: its settings, its instructions, and the tools it may use. A worker can change them on its card, so a reviewer gets the target branch's versions in its workspace, and a worker cannot steer its own review. The card's changes to them are still reviewed as code.</summary>
public static class SteeringFiles
{
	private static readonly HashSet<string> s_folders = new(StringComparer.OrdinalIgnoreCase) { ".claude", ".codex", ".opencode" };

	private static readonly HashSet<string> s_files = new(StringComparer.OrdinalIgnoreCase)
	{
		"CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", "AGENTS.override.md", ".mcp.json", "opencode.json", "opencode.jsonc",
	};

	/// <summary>Determines whether a path in a repository steers a harness.</summary>
	/// <param name="path">The path, with forward slashes, from the repository's root.</param>
	/// <returns><see langword="true"/> for a harness's settings or instructions, anywhere in the repository, including a steering folder's own entry, which a card could commit as a link.</returns>
	public static bool Contains(string path)
	{
		string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

		return parts.Length > 0 && (s_files.Contains(parts[^1]) || parts.Any(s_folders.Contains));
	}
}
