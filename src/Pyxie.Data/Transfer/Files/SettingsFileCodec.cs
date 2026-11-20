using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Pyxie.Data.Colors;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Internal;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer.Files.V1;
using Pyxie.Data.Transfer.Files.V3;

namespace Pyxie.Data.Transfer.Files;

/// <summary>Reads the frozen settings file versions and writes the current contract. File-shape errors are reported before the per-section editor rules are applied at import.</summary>
internal static class SettingsFileCodec
{
	// The file is downloaded as an attachment and never put in a page, so the characters a Dockerfile and agent arguments are full of, such as & and ', are written as they are.
	private static readonly JsonSerializerOptions s_options = new(SettingsJsonContextV1.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
	private static readonly JsonTypeInfo<SettingsDocumentV1> s_document = (JsonTypeInfo<SettingsDocumentV1>)s_options.GetTypeInfo(typeof(SettingsDocumentV1));

	private static readonly JsonSerializerOptions s_currentOptions = new(SettingsJsonContextV3.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
	private static readonly JsonTypeInfo<SettingsDocumentV3> s_current = (JsonTypeInfo<SettingsDocumentV3>)s_currentOptions.GetTypeInfo(typeof(SettingsDocumentV3));

	/// <summary>Reads a supported file whose envelope (format and version) was checked.</summary>
	/// <param name="version">The validated file version, which selects the harness vocabulary.</param>
	/// <param name="content">The file's UTF-8 content, without a byte order mark.</param>
	/// <returns>The settings, or a failure: the first shape problem, or every problem in a well-shaped file, one per line.</returns>
	public static ServiceResult<ProjectSettings> Read(ReadOnlySpan<byte> content, int version)
	{
		SettingsDocumentV1? document;
		try
		{
			if (version == 3)
			{
				SettingsDocumentV3? current = JsonSerializer.Deserialize(content, s_current);
				document = current is null ? null : new SettingsDocumentV1 { Agents = current.Agents?.ToList<AgentEntryV1?>(), Categories = current.Categories, Gates = current.Gates, Sandbox = current.Sandbox };
			}
			else
			{
				document = JsonSerializer.Deserialize(content, s_document);
			}
		}
		catch (JsonException exception)
		{
			string at = exception.Path is { Length: > 1 } path ? $" at {path.TrimStart('$', '.')}" : string.Empty;
			string line = exception.LineNumber is long number ? $" (line {number + 1})" : string.Empty;

			return ServiceResult<ProjectSettings>.Failure($"The settings file does not match file version {version}{at}{line}.");
		}

		if (document is null)
		{
			return ServiceResult<ProjectSettings>.Failure(SettingsFileReader.NotSettingsMessage);
		}

		List<string> problems = [];
		ProjectSettings settings = new(ReadAgents(document.Agents, version, problems), ReadGates(document.Gates, problems), ReadCategories(document.Categories, problems), ReadSandbox(document.Sandbox, problems));
		if (problems.Count > 0)
		{
			return ServiceResult<ProjectSettings>.Failure(string.Join("\n", problems));
		}

		return settings.Sections.Count == 0 ? ServiceResult<ProjectSettings>.Failure("The settings file holds no settings.") : ServiceResult<ProjectSettings>.Success(settings);
	}

	/// <summary>Writes settings in the current version: UTF-8 without a byte order mark, LF line breaks, a two-space indent, and a final line break.</summary>
	/// <param name="settings">The settings; a section that is <see langword="null"/> is left out.</param>
	/// <returns>The file's content.</returns>
	public static byte[] Write(ProjectSettings settings)
	{
		SettingsDocumentV3 document = new()
		{
			Format = SettingsFileFormat.Name,
			Version = SettingsFileFormat.CurrentVersion,
			Agents = settings.Agents?.Select(agent => (AgentEntryV3?)new AgentEntryV3 { Authentication = AuthenticationToken(agent.Authentication), Name = agent.Name, Harness = HarnessTokens.ToToken(agent.Harness), Command = agent.Command, Arguments = agent.Arguments, CanWork = agent.CanWork, CanReview = agent.CanReview, CanMerge = agent.CanMerge }).ToList(),
			Gates = settings.Gates?.Select(gate => (GateEntryV1?)new GateEntryV1 { Name = gate.Name, Command = gate.Command, Arguments = gate.Arguments, TimeoutMinutes = gate.TimeoutMinutes, Enabled = gate.IsEnabled }).ToList(),
			Categories = settings.Categories?.Select(category => (CategoryEntryV1?)new CategoryEntryV1 { Name = category.Name, Color = category.Color.ToString() }).ToList(),
			Sandbox = settings.Sandbox is SandboxSettingsInput sandbox ? new SandboxEntryV1 { Dockerfile = sandbox.Dockerfile } : null,
		};

		return [.. JsonSerializer.SerializeToUtf8Bytes(document, s_current), (byte)'\n'];
	}

	private static string AuthenticationToken(AgentAuthentication method) => method switch
	{
		AgentAuthentication.Account => "account",
		AgentAuthentication.ApiKey => "apiKey",
		AgentAuthentication.None => "none",
		_ => throw new ArgumentOutOfRangeException(nameof(method)),
	};

	private static List<AgentInput>? ReadAgents(List<AgentEntryV1?>? entries, int version, List<string> problems)
	{
		if (entries is null)
		{
			return null;
		}

		List<AgentInput> agents = [];
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index] is not AgentEntryV1 entry)
			{
				problems.Add($"Agent {index + 1} is empty.");
				continue;
			}

			string label = Label("Agent", entry.Name, index);
			int before = problems.Count;
			Require(entry.Name, "name", label, problems);
			Require(entry.Harness, "harness", label, problems);
			Require(entry.Command, "command", label, problems);
			Require(entry.Arguments, "arguments", label, problems);
			Require(entry.CanWork, "canWork", label, problems);
			Require(entry.CanReview, "canReview", label, problems);
			Require(entry.CanMerge, "canMerge", label, problems);

			HarnessKind harness = default;
			if (entry.Harness is string token && !HarnessTokens.TryRead(token, version, out harness))
			{
				problems.Add($"{label} has harness \"{token}\", which this Pyxie does not know. The harnesses are {EnglishLists.JoinWithAnd(version switch { 1 => HarnessTokens.Version1, 2 => HarnessTokens.Version2, _ => HarnessTokens.All })}.");
			}

			AgentAuthentication authentication;
			if (entry is AgentEntryV3 current)
			{
				authentication = current.Authentication switch { "account" => AgentAuthentication.Account, "apiKey" => AgentAuthentication.ApiKey, "none" => AgentAuthentication.None, _ => (AgentAuthentication)(-1) };
				if (!Enum.IsDefined(authentication))
				{
					problems.Add($"{label} needs an authentication method of account, apiKey, or none.");
				}
			}
			else
			{
				authentication = harness switch
				{
					HarnessKind.Other => AgentAuthentication.None,
					HarnessKind.DeepSeek when entry.Harness == "deepSeekApiKey" => AgentAuthentication.ApiKey,
					_ => AgentAuthentication.Account,
				};
			}

			if (problems.Count == before && entry is { Name: string name, Command: string command, Arguments: string arguments, CanWork: bool canWork, CanReview: bool canReview, CanMerge: bool canMerge })
			{
				agents.Add(new AgentInput(name, harness, command, arguments, canWork, canReview, canMerge) { Authentication = authentication });
			}
		}

		return agents;
	}

	private static List<GateInput>? ReadGates(List<GateEntryV1?>? entries, List<string> problems)
	{
		if (entries is null)
		{
			return null;
		}

		List<GateInput> gates = [];
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index] is not GateEntryV1 entry)
			{
				problems.Add($"Gate {index + 1} is empty.");
				continue;
			}

			string label = Label("Gate", entry.Name, index);
			int before = problems.Count;
			Require(entry.Name, "name", label, problems);
			Require(entry.Command, "command", label, problems);
			Require(entry.Arguments, "arguments", label, problems);
			Require(entry.TimeoutMinutes, "timeoutMinutes", label, problems);
			Require(entry.Enabled, "enabled", label, problems);

			if (problems.Count == before && entry is { Name: string name, Command: string command, Arguments: string arguments, TimeoutMinutes: int timeoutMinutes, Enabled: bool enabled })
			{
				gates.Add(new GateInput(name, command, arguments, timeoutMinutes, enabled));
			}
		}

		return gates;
	}

	private static List<CategoryInput>? ReadCategories(List<CategoryEntryV1?>? entries, List<string> problems)
	{
		if (entries is null)
		{
			return null;
		}

		List<CategoryInput> categories = [];
		for (int index = 0; index < entries.Count; index++)
		{
			if (entries[index] is not CategoryEntryV1 entry)
			{
				problems.Add($"Category {index + 1} is empty.");
				continue;
			}

			string label = Label("Category", entry.Name, index);
			int before = problems.Count;
			Require(entry.Name, "name", label, problems);
			Require(entry.Color, "color", label, problems);

			HexColor color = default;
			if (entry.Color is string written && !HexColor.TryParse(written, out color))
			{
				problems.Add($"{label} has color \"{written}\", which is not a color written #RRGGBB.");
			}

			if (problems.Count == before && entry is { Name: string name })
			{
				categories.Add(new CategoryInput(name, color));
			}
		}

		return categories;
	}

	private static SandboxSettingsInput? ReadSandbox(SandboxEntryV1? entry, List<string> problems)
	{
		if (entry is null)
		{
			return null;
		}

		if (entry.Dockerfile is null)
		{
			problems.Add("The sandbox section has no \"dockerfile\".");
		}

		return new SandboxSettingsInput(entry.Dockerfile);
	}

	// An item is named by its name where it has one, and otherwise by its place in its list, counted from 1.
	private static string Label(string kind, string? name, int index)
	{
		return string.IsNullOrWhiteSpace(name) ? $"{kind} {index + 1}" : $"{kind} '{name.Trim()}'";
	}

	private static void Require(object? value, string member, string label, List<string> problems)
	{
		if (value is null)
		{
			problems.Add($"{label} has no \"{member}\".");
		}
	}
}
