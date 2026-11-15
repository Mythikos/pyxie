using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>A repository service that reports the repository a test gives it, or none, for components that only read the open project's repository.</summary>
/// <param name="repository">The repository to report, or <see langword="null"/> for a project whose import has not connected one.</param>
internal sealed class FixedRepositoryService(Repository? repository) : IRepositoryService
{
	/// <summary>Creates a service that reports a repository whose target branch is the given one.</summary>
	/// <param name="targetBranch">The target branch.</param>
	/// <returns>The service.</returns>
	public static FixedRepositoryService WithTarget(string targetBranch)
	{
		return new FixedRepositoryService(new Repository { Id = 1, Path = Path.Combine(Path.GetTempPath(), "pyxie-hub"), Origin = "https://example.com/app.git", TargetBranch = targetBranch });
	}

	/// <inheritdoc/>
	public Task<Repository?> GetAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult(repository);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Repository>> ConnectAsync(RepositoryInput input, CancellationToken cancellationToken)
	{
		throw new NotSupportedException("Components under test only read the repository.");
	}
}
