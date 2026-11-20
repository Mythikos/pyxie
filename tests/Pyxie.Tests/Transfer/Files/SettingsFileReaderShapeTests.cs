using Pyxie.Tests.Support;

using Xunit;

using static Pyxie.Tests.Transfer.Files.SettingsFileText;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>A version 1 file holds exactly what version 1 names, each value of the kind it names: a member it does not name, a member name in another case, or a value of another kind is refused with the member's path and line, so a person can find it in the file.</summary>
public sealed class SettingsFileReaderShapeTests
{
	public static IEnumerable<TheoryDataRow<string, string>> Refusals { get; } =
	[
		Case("an unknown member in an item", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "agents": [
			    {
			      "name": "Claude",
			      "harness": "claudeCode",
			      "canWrok": true
			    }
			  ]
			}
			""", "The settings file does not match file version 1 at agents[0].canWrok (line 8)."),
		Case("an unknown member at the root", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "exportedBy": "Pyxie",
			  "categories": []
			}
			""", "The settings file does not match file version 1 at exportedBy (line 4)."),
		Case("a section named in another case", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "Categories": []
			}
			""", "The settings file does not match file version 1 at Categories (line 4)."),
		Case("a role written as a word", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "agents": [
			    {
			      "name": "Claude",
			      "canWork": "yes"
			    }
			  ]
			}
			""", "The settings file does not match file version 1 at agents[0].canWork (line 7)."),
		Case("a harness written as a number", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "agents": [
			    {
			      "name": "Claude",
			      "harness": 0
			    }
			  ]
			}
			""", "The settings file does not match file version 1 at agents[0].harness (line 7)."),
		Case("a timeout written as a string in the second gate", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "gates": [
			    { "name": "Build", "command": "dotnet", "arguments": "build", "timeoutMinutes": 30, "enabled": true },
			    { "name": "Tests", "command": "dotnet", "arguments": "test", "timeoutMinutes": "30", "enabled": true }
			  ]
			}
			""", "The settings file does not match file version 1 at gates[1].timeoutMinutes (line 6)."),
		Case("a fractional timeout", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "gates": [
			    { "name": "Build", "command": "dotnet", "arguments": "build", "timeoutMinutes": 1.5, "enabled": true }
			  ]
			}
			""", "The settings file does not match file version 1 at gates[0].timeoutMinutes (line 5)."),
		Case("a gate enabled by a number", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "gates": [
			    { "name": "Build", "command": "dotnet", "arguments": "build", "timeoutMinutes": 30, "enabled": 1 }
			  ]
			}
			""", "The settings file does not match file version 1 at gates[0].enabled (line 5)."),
		Case("a color written as a number", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": [
			    { "name": "Bug", "color": 16443884 }
			  ]
			}
			""", "The settings file does not match file version 1 at categories[0].color (line 5)."),
		Case("an item written as a string", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "agents": [
			    "Claude"
			  ]
			}
			""", "The settings file does not match file version 1 at agents[0] (line 5)."),
		Case("a section written as an object", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "categories": {}
			}
			""", "The settings file does not match file version 1 at categories (line 4)."),
		Case("the sandbox written as a list", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "sandbox": []
			}
			""", "The settings file does not match file version 1 at sandbox (line 4)."),
		Case("a Dockerfile written as a list of lines", """
			{
			  "format": "pyxie.project-settings",
			  "version": 1,
			  "sandbox": {
			    "dockerfile": [ "FROM ${PYXIE_BASE}" ]
			  }
			}
			""", "The settings file does not match file version 1 at sandbox.dockerfile (line 5)."),
	];

	[Theory]
	[MemberData(nameof(Refusals))]
	public void Read_RefusesWhatVersion1DoesNotHold_NamingThePathAndTheLine(string text, string expected)
	{
		Assert.Equal(expected, ResultAssert.Failed(Read(text)));
	}

	private static TheoryDataRow<string, string> Case(string description, string text, string expected)
	{
		return new TheoryDataRow<string, string>(text, expected) { TestDisplayName = description };
	}
}
