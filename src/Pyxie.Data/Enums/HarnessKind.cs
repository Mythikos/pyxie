namespace Pyxie.Data.Enums;

/// <summary>The local agent harness an agent runs through.</summary>
public enum HarnessKind
{
	/// <summary>Anthropic Claude Code.</summary>
	ClaudeCode = 0,

	/// <summary>OpenAI Codex.</summary>
	Codex = 1,

	/// <summary>Any other command-line harness.</summary>
	Other = 3,

	/// <summary>DeepSeek Harness.</summary>
	DeepSeek = 4,
}
