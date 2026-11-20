using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Pyxie.Data.Enums;

namespace Pyxie.Data.Transfer.Files;

/// <summary>Owns how a settings file names an agent's harness. The names are the file's own, never the enum's member names or numbers, which can change with the code; a harness added, renamed, or removed changes the file's vocabulary and so its version (<see cref="SettingsFileFormat"/>).</summary>
public static class HarnessTokens
{
	private static readonly IReadOnlyList<(HarnessKind Harness, string Token)> s_tokens =
	[
		(HarnessKind.ClaudeCode, "claudeCode"),
		(HarnessKind.Codex, "codex"),
		(HarnessKind.DeepSeek, "deepSeek"),
		(HarnessKind.Other, "other"),
	];

	/// <summary>Gets every harness name, in the order the harnesses are offered.</summary>
	public static IReadOnlyList<string> All { get; } = [.. s_tokens.Select(entry => entry.Token)];

	/// <summary>The version 1 vocabulary, including the retired harness.</summary>
	internal static IReadOnlyList<string> Version1 { get; } = ["claudeCode", "codex", "openCode", "other"];

	internal static IReadOnlyList<string> Version2 { get; } = ["claudeCode", "codex", "deepSeekApiKey", "deepSeekAccount", "other"];

	/// <summary>Gets the name a settings file gives a harness.</summary>
	/// <param name="harness">The harness.</param>
	/// <returns>The harness's name; a value no harness has is written as its number, so an export never fails and an import of it is refused by name.</returns>
	public static string ToToken(HarnessKind harness)
	{
		foreach ((HarnessKind kind, string token) in s_tokens)
		{
			if (kind == harness)
			{
				return token;
			}
		}

		// Preserve the saved command when exporting an agent from a database with the retired harness.
		return harness == RetiredHarnesses.OpenCode ? "other" : ((int)harness).ToString(CultureInfo.InvariantCulture);
	}

	internal static bool TryRead(string token, int version, out HarnessKind harness)
	{
		if (version == 1 && token == "openCode")
		{
			harness = HarnessKind.Other;
			return true;
		}

		if (version == 2 && token is "deepSeekApiKey" or "deepSeekAccount")
		{
			harness = HarnessKind.DeepSeek;
			return true;
		}

		return TryParse(token, out harness) && (version >= 3 || harness != HarnessKind.DeepSeek);
	}

	/// <summary>Reads a harness from the name a settings file gives it, exactly as written.</summary>
	/// <param name="token">The name.</param>
	/// <param name="harness">The harness, or the default when the name is not one.</param>
	/// <returns><see langword="true"/> when the name is a harness's.</returns>
	public static bool TryParse([NotNullWhen(true)] string? token, out HarnessKind harness)
	{
		foreach ((HarnessKind kind, string name) in s_tokens)
		{
			if (string.Equals(name, token, StringComparison.Ordinal))
			{
				harness = kind;

				return true;
			}
		}

		harness = default;

		return false;
	}
}
