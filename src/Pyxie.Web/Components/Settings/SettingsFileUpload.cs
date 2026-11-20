using Blazorise;

using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Data.Transfer.Files;

namespace Pyxie.Web.Components.Settings;

/// <summary>Reads a settings file a person picked in the browser. The browser's own size check is not trusted: a file larger than <see cref="SettingsFileFormat.MaxBytes"/> is refused before any of it is read, and no more than that is ever read.</summary>
public static class SettingsFileUpload
{
	/// <summary>Reads and parses a picked settings file.</summary>
	/// <param name="file">The file as the browser reports it.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The settings, or why the file was refused.</returns>
	public static async Task<ServiceResult<ProjectSettings>> ReadAsync(IFileEntry file, CancellationToken cancellationToken)
	{
		if (file.Size > SettingsFileFormat.MaxBytes)
		{
			return ServiceResult<ProjectSettings>.Failure(SettingsFileFormat.TooLargeMessage);
		}

		byte[] content;
		try
		{
			await using Stream stream = file.OpenReadStream(SettingsFileFormat.MaxBytes, cancellationToken);
			using MemoryStream buffer = new();
			await stream.CopyToAsync(buffer, cancellationToken);
			content = buffer.ToArray();
		}
		catch (Exception exception) when (exception is IOException or InvalidOperationException)
		{
			// Blazor's stream from the browser fails with InvalidOperationException when the browser cannot read the file, such as one changed, moved, or locked after it was picked, and with an IOException when what arrives breaks the stream's own rules.
			return ServiceResult<ProjectSettings>.Failure("The file could not be read.");
		}

		return SettingsFileReader.Read(content);
	}
}
