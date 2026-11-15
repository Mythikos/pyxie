using System.Text;

using Pyxie.Tests.Support;

namespace Pyxie.Tests.Runtime.Processes;

/// <summary>A temporary folder one test owns, with helpers to lay out files in it. Disposing it deletes everything, so tests never share folders or leave any behind.</summary>
public sealed class ScratchFolder : IAsyncDisposable
{
	/// <summary>Initializes a new instance of the <see cref="ScratchFolder"/> class, creating the folder.</summary>
	public ScratchFolder()
	{
		Folder = TempFolder.Create("pyxie-proc");
	}

	/// <summary>Gets the full path of the folder.</summary>
	public string Folder { get; }

	/// <summary>Gets the full path of something inside the folder; it need not exist.</summary>
	/// <param name="relativePath">The path from the folder.</param>
	/// <returns>The full path.</returns>
	public string Combine(string relativePath)
	{
		return Path.Combine(Folder, relativePath);
	}

	/// <summary>Creates a folder inside this one.</summary>
	/// <param name="relativePath">The path from the folder.</param>
	/// <returns>The full path of the new folder.</returns>
	public string CreateSubfolder(string relativePath)
	{
		string path = Combine(relativePath);
		Directory.CreateDirectory(path);

		return path;
	}

	/// <summary>Writes a file inside this folder, creating the folders it needs.</summary>
	/// <param name="relativePath">The path from the folder.</param>
	/// <param name="content">The text of the file, written as UTF-8 without a byte order mark; empty unless given.</param>
	/// <returns>The full path of the file.</returns>
	public string CreateFile(string relativePath, string content = "")
	{
		string path = Combine(relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Folder);
		File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

		return path;
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await TempFolder.DeleteAsync(Folder);
	}
}
