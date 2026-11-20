using System.Security.Cryptography;
using System.Text.Json;

using Pyxie.Data.Entities;

namespace Pyxie.Data.Services.Inputs;

/// <summary>Identifies the editable fields a caller read, including changes made by import or by another client.</summary>
public static class SettingsRevision
{
	internal const string ChangedMessage = "These settings changed. Reload the item before trying again.";

	/// <summary>Identifies this agent and its current definition.</summary>
	public static string Of(Agent agent) => Hash(new { agent.Id, Fields = AgentInput.From(agent) });

	/// <summary>Identifies this gate and its editable fields. Reordering alone leaves its definition unchanged.</summary>
	public static string Of(Gate gate) => Hash(new { gate.Id, Fields = GateInput.From(gate) });

	/// <summary>Identifies this category and its name and color.</summary>
	public static string Of(Category category) => Hash(new { category.Id, Fields = CategoryInput.From(category) });

	internal static string Hash<TValue>(TValue value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
