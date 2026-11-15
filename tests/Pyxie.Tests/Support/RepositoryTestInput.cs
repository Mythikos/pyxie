using Pyxie.Data.Enums;
using Pyxie.Data.Services.Inputs;

namespace Pyxie.Tests.Support;

/// <summary>Builds valid <see cref="RepositoryInput"/> values so a test names only the field it is about. Paths are absolute on every operating system and never touch the disk.</summary>
public static class RepositoryTestInput
{
	/// <summary>The origin of a repository imported from a URL unless a test gives another.</summary>
	public const string DefaultUrl = "https://example.com/team/app.git";

	/// <summary>Gets an absolute path under the temporary folder that no test writes to.</summary>
	/// <param name="name">The folder name.</param>
	/// <returns>A fully qualified path.</returns>
	public static string PathFor(string name)
	{
		return Path.Combine(Path.GetTempPath(), "pyxie-test-repositories", name);
	}

	/// <summary>Creates input for a repository imported from a folder that passes validation unless an argument says otherwise.</summary>
	/// <param name="path">The path of the hub; an absolute path named <c>folder-clone</c> unless given.</param>
	/// <param name="targetBranch">The target branch; <c>main</c> unless given.</param>
	/// <param name="origin">The folder the clone was made from; an absolute path named <c>source-folder</c> unless given.</param>
	/// <returns>The repository input.</returns>
	public static RepositoryInput Folder(string? path = null, string targetBranch = "main", string? origin = null)
	{
		return new RepositoryInput(RepositorySource.Folder, path ?? PathFor("folder-clone"), origin ?? PathFor("source-folder"), targetBranch);
	}

	/// <summary>Creates input for a repository imported from a URL that passes validation unless an argument says otherwise.</summary>
	/// <param name="path">The path of the hub; an absolute path named <c>url-clone</c> unless given.</param>
	/// <param name="targetBranch">The target branch; <c>main</c> unless given.</param>
	/// <param name="origin">The URL the clone was made from; <see cref="DefaultUrl"/> unless given.</param>
	/// <returns>The repository input.</returns>
	public static RepositoryInput Url(string? path = null, string targetBranch = "main", string origin = DefaultUrl)
	{
		return new RepositoryInput(RepositorySource.Url, path ?? PathFor("url-clone"), origin, targetBranch);
	}
}
