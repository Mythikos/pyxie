using System.Text.Json;

using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;

namespace Pyxie.Runtime.Harnesses;

/// <summary>Reads the result file an agent writes at the end of its round (see <see cref="PipelineContract.ResultFile"/>).</summary>
public static class HarnessResultFile
{
	/// <summary>The longest result file Pyxie reads: a note and a report, never this large.</summary>
	public const int MaxLength = 4 * 1024 * 1024;

	/// <summary>Reads the result file's text, as the sandbox handed it over.</summary>
	/// <param name="text">The file's text, read up to one character past <see cref="MaxLength"/>; <see langword="null"/> when the agent wrote none.</param>
	/// <param name="kind">The kind of round, which decides the outcome words.</param>
	/// <returns>The verdict, or why the file is unusable.</returns>
	public static (RoundVerdict? Verdict, string? Problem) Read(string? text, RoundKind kind)
	{
		if (text is null)
		{
			return (null, $"The agent did not write {PipelineContract.ResultFile}.");
		}

		if (text.Length > MaxLength)
		{
			return (null, $"{PipelineContract.ResultFile} is larger than 4 MB.");
		}

		// Some editors and tools start UTF-8 files with a byte order mark, which JSON does not allow.
		return Parse(text.TrimStart('\uFEFF'), kind);
	}

	/// <summary>Checks the text of a result file.</summary>
	/// <param name="text">The file's text.</param>
	/// <param name="kind">The kind of round the agent ran.</param>
	/// <returns>The verdict, or <see langword="null"/> with a reason when the text is not a valid result.</returns>
	public static (RoundVerdict? Verdict, string? Problem) Parse(string text, RoundKind kind)
	{
		string words = string.Join(", ", PipelineContract.OutcomeWords(kind));

		try
		{
			using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return (null, $"{PipelineContract.ResultFile} is not a JSON object.");
			}

			string? outcome = ReadString(root, "outcome");
			if (PipelineContract.ResultOf(kind, outcome) is not RoundResult result)
			{
				return (null, $"{PipelineContract.ResultFile} has outcome \"{outcome}\"; a {kind.DisplayName()} round reports one of: {words}.");
			}

			return (new RoundVerdict(result, ReadString(root, "note"), ReadString(root, "report")), null);
		}
		catch (JsonException exception)
		{
			return (null, $"{PipelineContract.ResultFile} is not valid JSON: {exception.Message}");
		}
	}

	private static string? ReadString(JsonElement root, string name)
	{
		foreach (JsonProperty property in root.EnumerateObject())
		{
			if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				return property.Value.ValueKind switch
				{
					JsonValueKind.String => property.Value.GetString(),
					JsonValueKind.Null or JsonValueKind.Undefined => null,
					_ => property.Value.GetRawText(),
				};
			}
		}

		return null;
	}
}
