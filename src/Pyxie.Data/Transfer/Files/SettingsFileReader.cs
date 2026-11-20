using System.Text.Json;
using System.Text.Unicode;

using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Transfer.Files;

/// <summary>Reads a settings file of any version this Pyxie knows into <see cref="ProjectSettings"/>. It first reads only the envelope, its format and version, so a file that is not a settings file, or one written by a newer Pyxie, is told apart from one this version cannot read; then it reads the file strictly by its version. Every version is well-formed JSON without comments, trailing commas, or repeated members. It checks the shape and encoding only: the rules an editor applies are checked when the settings are imported.</summary>
public static class SettingsFileReader
{
	/// <summary>The message for a file that is not a settings file at all.</summary>
	public const string NotSettingsMessage = "The file is not a Pyxie settings file.";

	private static readonly JsonDocumentOptions s_envelopeOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16, AllowDuplicateProperties = false };

	/// <summary>Reads a settings file.</summary>
	/// <param name="content">The file's content, UTF-8 with or without a byte order mark.</param>
	/// <returns>The settings, or a failure that says what is wrong with the file, one problem per line.</returns>
	public static ServiceResult<ProjectSettings> Read(ReadOnlyMemory<byte> content)
	{
		if (content.Length > SettingsFileFormat.MaxBytes)
		{
			return ServiceResult<ProjectSettings>.Failure(SettingsFileFormat.TooLargeMessage);
		}

		// Notepad and other editors start a UTF-8 file with a byte order mark, which the JSON reader does not skip.
		ReadOnlyMemory<byte> json = content.Span.StartsWith("﻿"u8) ? content[3..] : content;
		if (json.Span.Trim(" \t\r\n"u8).IsEmpty)
		{
			return ServiceResult<ProjectSettings>.Failure("The file is empty.");
		}

		// Every version is UTF-8; checked first, so text in another encoding, such as a JSON file saved as Latin-1, is refused here instead of failing later when a name or value is read.
		if (!Utf8.IsValid(json.Span))
		{
			return ServiceResult<ProjectSettings>.Failure($"{NotSettingsMessage[..^1]}: it is not UTF-8 text.");
		}

		int version;
		try
		{
			using JsonDocument document = JsonDocument.Parse(json, s_envelopeOptions);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out JsonElement format) || format.ValueKind != JsonValueKind.String || !string.Equals(format.GetString(), SettingsFileFormat.Name, StringComparison.Ordinal))
			{
				return ServiceResult<ProjectSettings>.Failure(NotSettingsMessage);
			}

			if (!root.TryGetProperty("version", out JsonElement written) || written.ValueKind != JsonValueKind.Number || !written.TryGetInt32(out version) || version < 1)
			{
				return ServiceResult<ProjectSettings>.Failure("The settings file's \"version\" must be a whole number of 1 or more.");
			}
		}
		catch (JsonException exception)
		{
			long? line = exception.LineNumber + 1 ?? RepeatedMemberLine(json.Span);

			return ServiceResult<ProjectSettings>.Failure(line is long known ? $"{NotSettingsMessage[..^1]}: it cannot be read at line {known}." : $"{NotSettingsMessage[..^1]}: it cannot be read.");
		}

		if (version > SettingsFileFormat.CurrentVersion)
		{
			return ServiceResult<ProjectSettings>.Failure($"This settings file was exported by a newer Pyxie (file version {version}). This Pyxie reads file versions up to {SettingsFileFormat.CurrentVersion}.");
		}

		return SettingsFileCodec.Read(json.Span, version);
	}

	// The parser refuses a member repeated in an object without saying where, so the file is read again to find the line of the repeat.
	private static long? RepeatedMemberLine(ReadOnlySpan<byte> json)
	{
		Utf8JsonReader reader = new(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
		Stack<HashSet<string>> objects = new();
		try
		{
			while (reader.Read())
			{
				if (reader.TokenType == JsonTokenType.StartObject)
				{
					objects.Push(new HashSet<string>(StringComparer.Ordinal));
				}
				else if (reader.TokenType == JsonTokenType.EndObject)
				{
					objects.Pop();
				}
				else if (reader.TokenType == JsonTokenType.PropertyName && !objects.Peek().Add(reader.GetString() ?? string.Empty))
				{
					return json[..(int)reader.TokenStartIndex].Count((byte)'\n') + 1;
				}
			}
		}
		catch (JsonException)
		{
		}

		return null;
	}
}
