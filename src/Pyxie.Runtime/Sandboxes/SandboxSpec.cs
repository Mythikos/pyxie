namespace Pyxie.Runtime.Sandboxes;

/// <summary>What a sandbox is made from and given. A sandbox sees nothing of the host but what is listed here: its input folder (read-only), its output folder, its volumes, its mounts, and its secrets. It reaches the internet and the local network, never the machine it runs on, and so never Pyxie.</summary>
/// <param name="Name">The sandbox's name, unique on the machine (see <see cref="SandboxNames"/>).</param>
/// <param name="Image">The image the sandbox is made from.</param>
/// <param name="ProjectKey">The project's key (see <see cref="SandboxNames.ProjectKey"/>), so a cleanup can find what belongs to it.</param>
/// <param name="CardId">The card the sandbox works for.</param>
/// <param name="Role">What the sandbox is for.</param>
/// <param name="InputFolder">The host folder Pyxie writes the round's input into.</param>
/// <param name="OutputFolder">The host folder the round's output comes back in.</param>
public sealed record SandboxSpec(string Name, string Image, string ProjectKey, int CardId, SandboxRole Role, string InputFolder, string OutputFolder)
{
	/// <summary>Gets the volumes the sandbox keeps its files in.</summary>
	public IReadOnlyList<SandboxVolume> Volumes { get; init; } = [];

	/// <summary>Gets the other host folders and files the sandbox sees.</summary>
	public IReadOnlyList<SandboxMount> Mounts { get; init; } = [];

	/// <summary>Gets the files whose text commands in the sandbox get as environment variables.</summary>
	public IReadOnlyList<SandboxSecret> Secrets { get; init; } = [];

	/// <summary>Gets the environment every command in the sandbox gets. Never put a secret here; use <see cref="Secrets"/>.</summary>
	public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

	/// <summary>Gets a value indicating whether the sandbox outlives its round: a card's worker is kept until the card is done.</summary>
	public bool Keep => Role == SandboxRole.Worker;
}
