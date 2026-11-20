using System.Text;

using Pyxie.Data.Colors;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Transfer.Files;

/// <summary>A settings file is UTF-8. Text in another encoding, such as a JSON file saved as Latin-1, is refused as such before anything in it is read, so no byte of it can fail the reader later.</summary>
public sealed class SettingsFileReaderEncodingTests
{
	private const string NotUtf8Message = "The file is not a Pyxie settings file: it is not UTF-8 text.";

	// 0xE9 is é in Latin-1 and Windows-1252, and is never a whole character in UTF-8.
	private const byte Latin1E = 0xE9;

	[Fact]
	public void Read_OfAFileSavedAsLatin1WithAnAccentInItsFormat_RefusesItAsNotUtf8()
	{
		byte[] content = [.. Encoding.ASCII.GetBytes("{\"format\": \"R"), Latin1E, .. Encoding.ASCII.GetBytes("sum"), Latin1E, .. Encoding.ASCII.GetBytes("\", \"version\": 1}")];

		Assert.Equal(NotUtf8Message, ResultAssert.Failed(SettingsFileReader.Read(content)));
	}

	[Fact]
	public void Read_OfAMemberNameThatIsNotUtf8BesideARepeatedMember_RefusesItAsNotUtf8()
	{
		byte[] content = [.. Encoding.ASCII.GetBytes("{\"format\": \"pyxie.project-settings\", \"version\": 1, \"n"), Latin1E, .. Encoding.ASCII.GetBytes("\": 1, \"version\": 1}")];

		Assert.Equal(NotUtf8Message, ResultAssert.Failed(SettingsFileReader.Read(content)));
	}

	[Fact]
	public void Read_OfASettingsFileWithAValueThatIsNotUtf8_RefusesItAsNotUtf8()
	{
		byte[] valid = SettingsFileWriter.Write(new ProjectSettings(null, null, [new CategoryInput("Bug", CategoryColors.Pink)], null));
		byte[] content = [.. Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid).Replace("\"Bug\"", "\"B?g\"", StringComparison.Ordinal))];
		content[Array.IndexOf(content, (byte)'?')] = Latin1E;

		ServiceResult<ProjectSettings> read = SettingsFileReader.Read(content);

		Assert.Equal(NotUtf8Message, ResultAssert.Failed(read));
	}

	[Fact]
	public void Read_OfAccentsWrittenAsUtf8_ReadsThem()
	{
		byte[] content = SettingsFileWriter.Write(new ProjectSettings(null, null, [new CategoryInput("Résumé", CategoryColors.Pink)], null));

		ProjectSettings settings = ResultAssert.Succeeded(SettingsFileReader.Read(content));

		Assert.Equal(["Résumé"], settings.Categories?.Select(category => category.Name) ?? []);
	}
}
