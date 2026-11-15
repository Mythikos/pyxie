using System.Reflection;
using System.Text;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>The files Pyxie's base image is built from, embedded in Pyxie: the Dockerfile and the scripts that start a sandbox, set its firewall, and run its commands. Their text is read with Unix line endings whatever the checkout did to them, because the scripts run in Linux.</summary>
public static class SandboxImageFiles
{
	private const string ResourcePrefix = "Pyxie.Sandboxes.Image.";

	private static readonly Lazy<IReadOnlyDictionary<string, string>> s_files = new(Load);
	private static readonly Lazy<string> s_hash = new(ComputeHash);

	/// <summary>Gets the files, by name.</summary>
	public static IReadOnlyDictionary<string, string> Files => s_files.Value;

	/// <summary>Gets a hash of the files, which tags the base image, so a Pyxie with different files builds its own.</summary>
	public static string Hash => s_hash.Value;

	private static Dictionary<string, string> Load()
	{
		Assembly assembly = typeof(SandboxImageFiles).Assembly;
		Dictionary<string, string> files = new(StringComparer.Ordinal);

		foreach (string resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
		{
			using Stream stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"The embedded file {resource} cannot be read.");
			using StreamReader reader = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			files[resource[ResourcePrefix.Length..]] = reader.ReadToEnd().ReplaceLineEndings("\n");
		}

		if (!files.ContainsKey("Dockerfile"))
		{
			throw new InvalidOperationException("Pyxie's base image Dockerfile is not embedded in Pyxie.Runtime.");
		}

		return files;
	}

	private static string ComputeHash()
	{
		StringBuilder all = new();
		foreach ((string name, string text) in Files.OrderBy(file => file.Key, StringComparer.Ordinal))
		{
			all.Append(name).Append('\0').Append(text).Append('\0');
		}

		return SandboxNames.Hash(all.ToString())[..16];
	}
}
