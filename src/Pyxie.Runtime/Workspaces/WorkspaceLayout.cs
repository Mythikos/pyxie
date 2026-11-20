using System.Globalization;

using Microsoft.Extensions.Options;

using Pyxie.Data.Persistence;

namespace Pyxie.Runtime.Workspaces;

/// <summary>Names the files and folders under the workspace root, and owns where the root is. The root holds the machine database, the sign-ins Pyxie hands to agents' sandboxes, and one folder per project; a project's folder holds its database, its hub (the bare repository), the host side of each card's sandboxes (their input, output, and sign-in folders), and the output of every round and image build.</summary>
public sealed class WorkspaceLayout
{
	private const string ProjectsFolderName = "projects";
	private const string HubFolderName = "hub";
	private const string SandboxesFolderName = "sandboxes";
	private const string RunsFolderName = "runs";
	private const string SecretsFolderName = "secrets";
	private const string ClaudeTokenFileName = "claude-oauth-token";

	/// <summary>Initializes a new instance of the <see cref="WorkspaceLayout"/> class.</summary>
	/// <param name="options">The workspace options.</param>
	/// <exception cref="InvalidOperationException"><c>Workspace:Root</c> is set but is not a fully qualified path.</exception>
	public WorkspaceLayout(IOptions<WorkspaceOptions> options)
	{
		string configured = options.Value.Root.Trim();
		if (configured.Length > 0 && !Path.IsPathFullyQualified(configured))
		{
			throw new InvalidOperationException($"'{WorkspaceOptions.SectionName}:{nameof(WorkspaceOptions.Root)}' must be a full path; '{configured}' is not.");
		}

		Root = configured.Length > 0 ? Path.GetFullPath(configured) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Pyxie");
	}

	/// <summary>Gets the workspace root.</summary>
	public string Root { get; }

	/// <summary>Gets the machine database file, which lists the projects.</summary>
	public string MachineDatabasePath => DatabaseFiles.PathIn(Root);

	/// <summary>Gets the folder that holds one folder per project.</summary>
	public string ProjectsFolder => Path.Combine(Root, ProjectsFolderName);

	/// <summary>Gets the folder that holds the sign-ins Pyxie hands to agents' sandboxes.</summary>
	public string SecretsFolder => Path.Combine(Root, SecretsFolderName);

	/// <summary>Gets the file that holds the Claude Code token <c>claude setup-token</c> printed.</summary>
	public string ClaudeTokenPath => Path.Combine(SecretsFolder, ClaudeTokenFileName);

	/// <summary>Gets the dedicated Anthropic API key file.</summary>
	public string ClaudeApiKeyPath => Path.Combine(SecretsFolder, "anthropic-api-key");

	/// <summary>Gets the dedicated OpenAI API key file.</summary>
	public string CodexApiKeyPath => Path.Combine(SecretsFolder, "openai-api-key");

	/// <summary>Gets the DeepSeek API key file.</summary>
	public string DeepSeekApiKeyPath => Path.Combine(SecretsFolder, "deepseek-api-key");

	/// <summary>Gets a project's hub: the bare repository the pipeline writes to and people clone, pull from, and push to.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <returns>The full path.</returns>
	public static string HubPath(string projectFolder)
	{
		return Path.Combine(projectFolder, HubFolderName);
	}

	/// <summary>Gets the folder that holds the host side of a project's card sandboxes.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <returns>The full path.</returns>
	public static string SandboxesFolder(string projectFolder)
	{
		return Path.Combine(projectFolder, SandboxesFolderName);
	}

	/// <summary>Gets the folder that holds the host side of a card's sandboxes.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="cardId">The card identifier.</param>
	/// <returns>The full path.</returns>
	public static string CardSandboxesFolder(string projectFolder, int cardId)
	{
		return Path.Combine(SandboxesFolder(projectFolder), CardFolderName(cardId));
	}

	/// <summary>Gets the folder that holds the host side of one of a card's sandboxes: its input, its output, and its sign-in.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="role">The sandbox's role, as it is labelled (see <c>SandboxNames.RoleName</c>).</param>
	/// <returns>The full path.</returns>
	public static string SandboxFolder(string projectFolder, int cardId, string role)
	{
		return Path.Combine(CardSandboxesFolder(projectFolder, cardId), role);
	}

	/// <summary>Gets the file that keeps the output of the last build of the project's sandbox image.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <returns>The full path.</returns>
	public static string ImageLogPath(string projectFolder)
	{
		return Path.Combine(projectFolder, RunsFolderName, "image.log");
	}

	/// <summary>Gets the file that keeps the full output of a round.</summary>
	/// <param name="projectFolder">The project's folder.</param>
	/// <param name="cardId">The card identifier.</param>
	/// <param name="sequence">The round's sequence in the card's history.</param>
	/// <returns>The full path.</returns>
	public static string RoundLogPath(string projectFolder, int cardId, int sequence)
	{
		return Path.Combine(projectFolder, RunsFolderName, CardFolderName(cardId), string.Create(CultureInfo.InvariantCulture, $"round-{sequence}.log"));
	}

	private static string CardFolderName(int cardId)
	{
		return string.Create(CultureInfo.InvariantCulture, $"card-{cardId}");
	}
}
