using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Names and labels for what sandboxes leave in the container engine. Names start with the project's key, a hash of its folder, so two projects, or two Pyxie workspace roots on one machine, never share a container or a volume.</summary>
public static class SandboxNames
{
	/// <summary>The label that carries the project's key.</summary>
	public const string ProjectLabel = "pyxie.project";

	/// <summary>The label that carries the card's identifier.</summary>
	public const string CardLabel = "pyxie.card";

	/// <summary>The label that carries what the sandbox is for.</summary>
	public const string RoleLabel = "pyxie.role";

	/// <summary>The label that carries a hash of everything a sandbox was made with, so a kept one is only picked up again when nothing changed.</summary>
	public const string SpecLabel = "pyxie.spec";

	/// <summary>The repository name of Pyxie's base image.</summary>
	public const string BaseImage = "pyxie-base";

	/// <summary>Gets a project's key: a short hash of its folder, stable for as long as the folder is.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <returns>Twelve lowercase hexadecimal characters.</returns>
	public static string ProjectKey(string projectFolder)
	{
		string folder = Path.GetFullPath(projectFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (OperatingSystem.IsWindows())
		{
			folder = folder.ToUpperInvariant();
		}

		return Hash(folder)[..12];
	}

	/// <summary>Gets the name of a card's sandbox for a role.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="role">What the sandbox is for.</param>
	/// <returns>The container's name.</returns>
	public static string Container(string projectKey, int cardId, SandboxRole role)
	{
		return string.Create(CultureInfo.InvariantCulture, $"pyxie-{projectKey}-card-{cardId}-{RoleName(role)}");
	}

	/// <summary>Gets the name of one of a card's volumes.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <param name="cardId">The card.</param>
	/// <param name="purpose">What the volume holds, such as <c>work</c> or <c>home</c>.</param>
	/// <returns>The volume's name.</returns>
	public static string Volume(string projectKey, int cardId, string purpose)
	{
		return string.Create(CultureInfo.InvariantCulture, $"pyxie-{projectKey}-card-{cardId}-{purpose}");
	}

	/// <summary>Gets the repository name of a project's image.</summary>
	/// <param name="projectKey">The project's key.</param>
	/// <returns>The image's repository name, without a tag.</returns>
	public static string ProjectImage(string projectKey)
	{
		return $"pyxie-project-{projectKey}";
	}

	/// <summary>Gets the word a role is labelled with.</summary>
	/// <param name="role">The role.</param>
	/// <returns>The label's value.</returns>
	public static string RoleName(SandboxRole role)
	{
		return role switch
		{
			SandboxRole.Worker => "work",
			SandboxRole.Gates => "gates",
			SandboxRole.Review => "review",
			SandboxRole.Resolve => "resolve",
			_ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown sandbox role."),
		};
	}

	/// <summary>Hashes text into lowercase hexadecimal, for tags and labels.</summary>
	/// <param name="text">The text.</param>
	/// <returns>The SHA-256 hash.</returns>
	public static string Hash(string text)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
	}
}
