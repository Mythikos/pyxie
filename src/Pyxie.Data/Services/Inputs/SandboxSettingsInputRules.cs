using System.Text.RegularExpressions;

using Pyxie.Data.Limits;
using Pyxie.Data.Sandboxes;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The rules for a <see cref="SandboxSettingsInput"/>, shared by the service and the settings page so both report the same messages.</summary>
public static partial class SandboxSettingsInputRules
{
	/// <summary>Gets a Dockerfile as it is checked and stored: Unix line endings, no blank space around it, and one final line break, as a file ends; empty for a blank one.</summary>
	/// <param name="dockerfile">The Dockerfile as entered.</param>
	/// <returns>The normalized text.</returns>
	public static string NormalizeDockerfile(string? dockerfile)
	{
		string text = (dockerfile ?? string.Empty).ReplaceLineEndings("\n").Trim();

		return text.Length == 0 ? string.Empty : text + "\n";
	}

	/// <summary>Checks a Dockerfile, as it will be stored: it must be there, fit the limit, and start a stage from Pyxie's base image, which holds what every sandbox needs.</summary>
	/// <param name="dockerfile">The Dockerfile.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when it is valid.</returns>
	public static string? ValidateDockerfile(string? dockerfile)
	{
		string text = NormalizeDockerfile(dockerfile);
		if (text.Length == 0)
		{
			return "Enter the project's Dockerfile.";
		}

		if (text.Length > PyxieLimits.MaxDockerfileLength)
		{
			return $"The Dockerfile must be {PyxieLimits.MaxDockerfileLength} characters or fewer.";
		}

		return FromBaseImage().IsMatch(text) ? null : $"The Dockerfile must start FROM ${{{SandboxDefaults.BaseImageArgument}}}, Pyxie's base image.";
	}

	[GeneratedRegex(@"^[ \t]*FROM[ \t]+\$(\{PYXIE_BASE\}|PYXIE_BASE)(\s|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex FromBaseImage();
}
