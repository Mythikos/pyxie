using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Internal;

namespace Pyxie.Data.Services.Inputs;

/// <summary>The field rules for a <see cref="RepositoryInput"/>, shared by the repository service and the repository page so both report the same messages.</summary>
public static class RepositoryInputRules
{
	/// <summary>Checks that the source is one of the known ways to set up a repository.</summary>
	/// <param name="source">The source.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the source is defined.</returns>
	public static string? ValidateSource(RepositorySource source)
	{
		return Enum.IsDefined(source) ? null : "Source is not recognized.";
	}

	/// <summary>Checks a repository path after trimming it. It must be absolute, because the pipeline runs git from other folders and a relative path would depend on where Pyxie was started.</summary>
	/// <param name="path">The raw path.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the path is valid.</returns>
	public static string? ValidatePath(string? path)
	{
		string normalized = InputRules.Normalize(path);

		return InputRules.ValidateRequired(normalized, "Path", PyxieLimits.MaxPathLength)
			?? (System.IO.Path.IsPathFullyQualified(normalized) ? null : "Path must be an absolute path.");
	}

	/// <summary>Checks the folder or URL a repository is imported from, after trimming it: a folder must be a full path, and either is required.</summary>
	/// <param name="source">The kind of source. An undefined source has no origin rule; <see cref="ValidateSource"/> reports it.</param>
	/// <param name="origin">The raw folder or URL.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the origin suits the source.</returns>
	public static string? ValidateOrigin(RepositorySource source, string? origin)
	{
		string normalized = InputRules.Normalize(origin);

		return source switch
		{
			RepositorySource.Folder => InputRules.ValidateRequired(normalized, "Folder", PyxieLimits.MaxOriginLength)
				?? (System.IO.Path.IsPathFullyQualified(normalized) ? null : "Folder must be a full path."),
			RepositorySource.Url => InputRules.ValidateRequired(normalized, "URL", PyxieLimits.MaxOriginLength),
			_ => null,
		};
	}

	/// <summary>Checks a target branch name after trimming it. Whether the branch exists is decided by the runtime.</summary>
	/// <param name="targetBranch">The raw branch name.</param>
	/// <returns>A user-presentable message, or <see langword="null"/> when the name is valid.</returns>
	public static string? ValidateTargetBranch(string? targetBranch)
	{
		string normalized = InputRules.Normalize(targetBranch);

		return InputRules.ValidateRequired(normalized, "Target branch", PyxieLimits.MaxBranchNameLength)
			?? (normalized.Any(char.IsWhiteSpace) ? "Target branch cannot contain whitespace." : null);
	}
}
