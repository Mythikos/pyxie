using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pyxie.Data.Transfer.Files.V3;

/// <summary>The serializer for version 3 settings files, generated at compile time so the contract does not depend on reflection. It writes LF line breaks and a two-space indent on every machine, and refuses what version 3 does not hold: an unknown member, a repeated member, a comment, or a trailing comma.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, IndentSize = 2, NewLine = "\n", UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, AllowDuplicateProperties = false, AllowTrailingCommas = false, ReadCommentHandling = JsonCommentHandling.Disallow, PropertyNameCaseInsensitive = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, MaxDepth = 16)]
[JsonSerializable(typeof(SettingsDocumentV3))]
internal sealed partial class SettingsJsonContextV3 : JsonSerializerContext;
