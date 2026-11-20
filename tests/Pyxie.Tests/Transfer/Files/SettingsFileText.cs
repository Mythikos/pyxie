using System.Text;

using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>Builds settings files as text for the reader's tests, one line each, so a test states only the part of the file it is about: a version 1 envelope around the members given, and items with every member version 1 requires unless one is left out.</summary>
public static class SettingsFileText
{
	/// <summary>Reads a settings file given as text, encoded as UTF-8.</summary>
	/// <param name="text">The file's text.</param>
	/// <returns>What the reader made of it.</returns>
	public static ServiceResult<ProjectSettings> Read(string text)
	{
		return SettingsFileReader.Read(Encoding.UTF8.GetBytes(text));
	}

	/// <summary>Gets a version 1 settings file holding the members given after its format and version.</summary>
	/// <param name="members">The root members, such as a section from <see cref="Section"/>.</param>
	/// <returns>The file's text.</returns>
	public static string FileWith(params string[] members)
	{
		return $"{{ \"format\": \"pyxie.project-settings\", \"version\": 1, {string.Join(", ", members)} }}";
	}

	/// <summary>Gets a section member holding a list of items.</summary>
	/// <param name="name">The section's member name, such as <c>agents</c>.</param>
	/// <param name="items">The items, as JSON.</param>
	/// <returns>The member's text.</returns>
	public static string Section(string name, params string[] items)
	{
		return $"\"{name}\": [ {string.Join(", ", items)} ]";
	}

	/// <summary>Gets an agent with every member version 1 requires, all of them valid.</summary>
	/// <param name="name">The agent's name.</param>
	/// <param name="without">A member to leave out, or <see langword="null"/>.</param>
	/// <param name="harness">The harness member's JSON value.</param>
	/// <returns>The agent as JSON.</returns>
	public static string Agent(string name, string? without = null, string harness = "\"claudeCode\"")
	{
		return Item(without, ("name", Quoted(name)), ("harness", harness), ("command", "\"claude\""), ("arguments", "\"-p\""), ("canWork", "true"), ("canReview", "true"), ("canMerge", "false"));
	}

	/// <summary>Gets a gate with every member version 1 requires, all of them valid.</summary>
	/// <param name="name">The gate's name.</param>
	/// <param name="without">A member to leave out, or <see langword="null"/>.</param>
	/// <returns>The gate as JSON.</returns>
	public static string Gate(string name, string? without = null)
	{
		return Item(without, ("name", Quoted(name)), ("command", "\"dotnet\""), ("arguments", "\"test\""), ("timeoutMinutes", "30"), ("enabled", "true"));
	}

	/// <summary>Gets a category with every member version 1 requires.</summary>
	/// <param name="name">The category's name.</param>
	/// <param name="without">A member to leave out, or <see langword="null"/>.</param>
	/// <param name="color">The color member's text, written as a JSON string.</param>
	/// <returns>The category as JSON.</returns>
	public static string Category(string name, string? without = null, string color = "#FBE9EC")
	{
		return Item(without, ("name", Quoted(name)), ("color", Quoted(color)));
	}

	private static string Item(string? without, params (string Member, string Value)[] members)
	{
		return $"{{ {string.Join(", ", members.Where(member => member.Member != without).Select(member => $"\"{member.Member}\": {member.Value}"))} }}";
	}

	private static string Quoted(string value)
	{
		return $"\"{value}\"";
	}
}
