using Pyxie.Data.Enums;

namespace Pyxie.Web.Components.Settings.Models;

/// <summary>Describes how each <see cref="HarnessKind"/> is presented and pre-filled in the settings page.</summary>
public static class HarnessInfo
{
	/// <summary>Gets every harness kind in display order.</summary>
	public static IReadOnlyList<HarnessKind> All { get; } = Enum.GetValues<HarnessKind>();

	/// <summary>Gets the display name of a harness kind.</summary>
	/// <param name="harness">The harness kind.</param>
	/// <returns>The user-facing name.</returns>
	public static string DisplayName(HarnessKind harness) => harness switch
	{
		HarnessKind.ClaudeCode => "Claude Code",
		HarnessKind.Codex => "Codex",
		HarnessKind.OpenCode => "OpenCode",
		_ => "Other",
	};

	/// <summary>Gets the executable suggested for a harness kind.</summary>
	/// <param name="harness">The harness kind.</param>
	/// <returns>The default command, or an empty string when there is none.</returns>
	public static string DefaultCommand(HarnessKind harness) => harness switch
	{
		HarnessKind.ClaudeCode => "claude",
		HarnessKind.Codex => "codex",
		HarnessKind.OpenCode => "opencode",
		_ => string.Empty,
	};

	/// <summary>Gets the arguments suggested for a harness kind: the ones that run it unattended with every permission, which the pipeline needs because nobody can answer a prompt. The agent runs in a sandbox that is the boundary (see <c>docs/sandboxing.md</c>), so the harness's own sandbox is turned off.</summary>
	/// <param name="harness">The harness kind.</param>
	/// <returns>The default arguments, or an empty string when there are none.</returns>
	public static string DefaultArguments(HarnessKind harness) => harness switch
	{
		HarnessKind.ClaudeCode => "-p --dangerously-skip-permissions",
		HarnessKind.Codex => "exec --dangerously-bypass-approvals-and-sandbox --skip-git-repo-check",
		HarnessKind.OpenCode => "run",
		_ => string.Empty,
	};
}
