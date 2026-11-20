using System.Buffers.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Reads a file-backed Codex ChatGPT login and builds the account-only credential for a round.</summary>
internal static class CodexLogin
{
	internal static string PathOnHost
	{
		get
		{
			string? home = Environment.GetEnvironmentVariable("CODEX_HOME");
			return Path.Combine(string.IsNullOrWhiteSpace(home) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex") : home, "auth.json");
		}
	}

	internal static string? ReadAccount(string path)
	{
		try
		{
			using StreamReader reader = File.OpenText(path);
			using JsonDocument document = JsonDocument.Parse(reader.ReadToEnd(), new JsonDocumentOptions { AllowDuplicateProperties = false });
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !IsAccount(root) || !root.TryGetProperty("tokens", out JsonElement tokens) || tokens.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			if (!HasIdTokenPayload(StringField(tokens, "id_token")) ||
				string.IsNullOrWhiteSpace(StringField(tokens, "access_token")) ||
				string.IsNullOrWhiteSpace(StringField(tokens, "refresh_token")))
			{
				return null;
			}

			// Codex keeps the OAuth client and login owner inside tokens. The other provider credentials live at the root.
			JsonObject account = new() { ["auth_mode"] = "chatgpt", ["tokens"] = JsonNode.Parse(tokens.GetRawText()) };
			if (root.TryGetProperty("last_refresh", out JsonElement refreshed) && refreshed.ValueKind != JsonValueKind.Null)
			{
				if (refreshed.ValueKind != JsonValueKind.String || !refreshed.TryGetDateTimeOffset(out _))
				{
					return null;
				}

				account["last_refresh"] = refreshed.GetString();
			}

			return account.ToJsonString();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FormatException)
		{
			return null;
		}
	}

	private static bool HasIdTokenPayload(string? token)
	{
		if (token?.Split('.') is not [{ Length: > 0 }, { Length: > 0 } payload, { Length: > 0 }])
		{
			return false;
		}

		using JsonDocument claims = JsonDocument.Parse(Base64Url.DecodeFromChars(payload));
		return claims.RootElement.ValueKind == JsonValueKind.Object;
	}

	private static bool IsAccount(JsonElement root)
	{
		if (root.TryGetProperty("auth_mode", out JsonElement mode) && mode.ValueKind != JsonValueKind.Null)
		{
			return mode.ValueKind == JsonValueKind.String && mode.GetString() == "chatgpt";
		}

		// Older Codex files infer their method. A token bundle alone identifies a ChatGPT login.
		return !root.EnumerateObject().Any(field => field.Name is ("OPENAI_API_KEY" or "personal_access_token" or "bedrock_api_key" or "bedrock_access_keys") && field.Value.ValueKind != JsonValueKind.Null);
	}

	private static string? StringField(JsonElement parent, string name) => parent.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
}
