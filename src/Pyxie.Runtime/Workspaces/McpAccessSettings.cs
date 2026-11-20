using Pyxie.Data.Services.Results;

namespace Pyxie.Runtime.Workspaces;

/// <summary>The local operator opts this Pyxie host into MCP by enabling it in Settings. The presence of one empty file in the workspace root is the durable switch.</summary>
public sealed class McpAccessSettings(WorkspaceLayout layout)
{
	private readonly string _path = Path.Combine(layout.Root, "mcp.enabled");

	/// <summary>Raised after the operator changes access in any settings tab.</summary>
	public event Action? Changed;

	/// <summary>Gets whether this host accepts MCP requests. An absent or unreadable path leaves the endpoint disabled.</summary>
	public bool Enabled => File.Exists(_path);

	/// <summary>Persists the operator's choice for this workspace.</summary>
	/// <param name="enabled">Whether to accept local MCP clients.</param>
	/// <returns>The result of persisting the switch.</returns>
	public ServiceResult SetEnabled(bool enabled)
	{
		try
		{
			if (enabled)
			{
				using FileStream marker = File.Open(_path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
			}
			else
			{
				File.Delete(_path);
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return ServiceResult.Failure("Could not save MCP access. Check write access to the Pyxie workspace root.");
		}

		Changed?.Invoke();
		return ServiceResult.Success();
	}
}
