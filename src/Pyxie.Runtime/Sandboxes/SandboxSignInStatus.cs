namespace Pyxie.Runtime.Sandboxes;

/// <summary>Whether the sign-ins Pyxie hands to agents' sandboxes are there, and where Pyxie looks for them.</summary>
/// <param name="ClaudeTokenPath">The file that holds the Claude Code token.</param>
/// <param name="ClaudeTokenFound">Whether that file is there and not empty.</param>
/// <param name="CodexSignInPath">The Codex sign-in Pyxie copies into each sandbox.</param>
/// <param name="CodexSignInFound">Whether it is there.</param>
public sealed record SandboxSignInStatus(string ClaudeTokenPath, bool ClaudeTokenFound, string CodexSignInPath, bool CodexSignInFound);
