using System.Text;

using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Settings;

using Xunit;

using static Pyxie.Tests.Web.SettingsTransferTestData;

namespace Pyxie.Tests.Web;

/// <summary>A picked settings file is read without trusting the browser: a file it reports as larger than the limit is refused before any of it is read, a read never asks for more than the limit, and a browser that stops sending the file gives a message rather than an error.</summary>
public sealed class SettingsFileUploadTests
{
	[Fact]
	public async Task ReadAsync_OfAFileReportedLargerThanTheLimit_RefusesItWithoutOpeningIt()
	{
		PickedSettingsFile file = new(() => new MemoryStream(SettingsFileWriter.Write(FileAdding(TransferSection.Agents))), SettingsFileFormat.MaxBytes + 1);

		ServiceResult<ProjectSettings> read = await SettingsFileUpload.ReadAsync(file, TestContext.Current.CancellationToken);

		Assert.Equal(SettingsFileFormat.TooLargeMessage, ResultAssert.Failed(read));
		Assert.Equal(0, file.Opens);
	}

	[Fact]
	public async Task ReadAsync_OfASettingsFile_ReadsEverySectionItHoldsAndAsksForNoMoreThanTheLimit()
	{
		ProjectSettings settings = new(Agents: [Agent("Claude"), Agent("Codex", canReview: false)], Gates: [Gate("Build"), Gate("Test")], Categories: SeededCategoriesWithoutChore, Sandbox: new SandboxSettingsInput(ImportedDockerfile));
		PickedSettingsFile file = PickedSettingsFile.Of(settings);

		ProjectSettings read = ResultAssert.Succeeded(await SettingsFileUpload.ReadAsync(file, TestContext.Current.CancellationToken));

		Assert.Equal(settings.Agents, read.Agents);
		Assert.Equal(settings.Gates, read.Gates);
		Assert.Equal(settings.Categories, read.Categories);
		Assert.Equal(settings.Sandbox, read.Sandbox);
		Assert.Equal(1, file.Opens);
		Assert.Equal(SettingsFileFormat.MaxBytes, file.MaxAllowedSize);
	}

	[Fact]
	public async Task ReadAsync_OfAFileOfExactlyTheLimit_ReadsIt()
	{
		byte[] written = SettingsFileWriter.Write(FileAdding(TransferSection.Gates));
		byte[] content = [.. written, .. Enumerable.Repeat((byte)' ', SettingsFileFormat.MaxBytes - written.Length)];
		PickedSettingsFile file = new(content);

		ProjectSettings read = ResultAssert.Succeeded(await SettingsFileUpload.ReadAsync(file, TestContext.Current.CancellationToken));

		Assert.Equal(SettingsFileFormat.MaxBytes, content.Length);
		Assert.Equal([Gate("Build")], read.Gates);
	}

	[Fact]
	public async Task ReadAsync_OfAFileThatIsNotASettingsFile_SaysWhatTheReaderFound()
	{
		PickedSettingsFile file = new(Encoding.UTF8.GetBytes("""{ "name": "app" }"""));

		ServiceResult<ProjectSettings> read = await SettingsFileUpload.ReadAsync(file, TestContext.Current.CancellationToken);

		Assert.Equal(SettingsFileReader.NotSettingsMessage, ResultAssert.Failed(read));
	}

	[Theory]
	[InlineData("browser read error")]
	[InlineData("broken stream")]
	public async Task ReadAsync_WhenTheFileCannotBeReadToItsEnd_SaysTheFileCouldNotBeRead(string failure)
	{
		Exception thrown = failure == "browser read error" ? new InvalidOperationException("An error occurred while reading the remote stream: NotReadableError") : new EndOfStreamException();
		PickedSettingsFile file = new(() => new InterruptedUploadStream(thrown), 512);

		ServiceResult<ProjectSettings> read = await SettingsFileUpload.ReadAsync(file, TestContext.Current.CancellationToken);

		Assert.Equal("The file could not be read.", ResultAssert.Failed(read));
		Assert.Equal(1, file.Opens);
	}

	[Fact]
	public async Task ReadAsync_WhenCancelled_ThrowsInsteadOfSayingTheFileCouldNotBeRead()
	{
		PickedSettingsFile file = PickedSettingsFile.Of(FileAdding(TransferSection.Agents));
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SettingsFileUpload.ReadAsync(file, cancelled.Token));
	}
}
