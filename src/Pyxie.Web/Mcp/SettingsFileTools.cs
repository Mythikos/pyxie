using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Pyxie.Data.Services;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Projects;

namespace Pyxie.Web.Mcp;

internal sealed class SettingsFileTools(PipelineController pipeline, PipelineActivity activity, ISettingsTransferService transfer)
{
	[McpServerTool(ReadOnly = true), Description("Export project settings as the exact JSON settings-file text accepted by Pyxie.")]
	public async Task<string> ExportSettingsAsync(int projectId, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		ProjectSettings settings = (await transfer.ExportAsync(projectId, cancellationToken)).Value();
		return Encoding.UTF8.GetString(SettingsFileWriter.Write(settings));
	}

	[McpServerTool(ReadOnly = true), Description("Preview settings-file JSON. Inspect additions, changes, removals and blockers. Each section has a previewId to pass to import_settings.")]
	public async Task<object> PreviewSettingsImportAsync(int projectId, string file, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		ProjectSettings settings = Read(file);
		SettingsImportPreview preview = (await transfer.PreviewAsync(projectId, settings, cancellationToken)).Value();
		string fileHash = FileHash(file);
		return new { Sections = preview.Sections.Select(section => new { section.Section, section.Changes, section.Problems, section.CanImport, PreviewId = PreviewId(projectId, fileHash, section) }).ToArray() };
	}

	[McpServerTool(Destructive = true), Description("Replace selected settings sections using the reviewed file text. Pass sections as an object mapping each chosen section name (Agents, Gates, Categories, Sandbox) to its previewId from preview_settings_import.")]
	public async Task<string> ImportSettingsAsync(int projectId, string file, Dictionary<TransferSection, string> sections, CancellationToken cancellationToken)
	{
		using ProjectLease lease = pipeline.AcquireProject(projectId).Value();
		ProjectSettings settings = Read(file);
		SettingsImportPreview preview = (await transfer.PreviewAsync(projectId, settings, cancellationToken)).Value();
		string fileHash = FileHash(file);
		foreach ((TransferSection section, string id) in sections)
		{
			if (preview.Find(section) is not SectionPreview current || !string.Equals(id, PreviewId(projectId, fileHash, current), StringComparison.Ordinal))
			{
				throw new McpException("The file, project or selected section changed. Preview the import again.");
			}
		}

		HashSet<TransferSection> selected = [.. sections.Keys];
		string result = (await transfer.ImportAsync(settings, preview, selected, cancellationToken)).Done();
		foreach (SectionPreview section in preview.Sections.Where(section => selected.Contains(section.Section) && section.Changes.HasChanges))
		{
			activity.NotifySettingsChanged(projectId, section.Section);
		}

		return result;
	}

	private static ProjectSettings Read(string file)
	{
		if (Encoding.UTF8.GetByteCount(file) > SettingsFileFormat.MaxBytes)
		{
			throw new McpException(SettingsFileFormat.TooLargeMessage);
		}

		return SettingsFileReader.Read(Encoding.UTF8.GetBytes(file)).Value();
	}

	private static string FileHash(string file) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(file)));

	private static string PreviewId(int projectId, string fileHash, SectionPreview section) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { projectId, fileHash, section })));
}
