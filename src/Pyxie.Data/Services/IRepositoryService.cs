using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Records the repository of the open project. A project has exactly one, written when it is imported, and it never changes afterwards. Pyxie only records it: the runtime sets the hub up before it connects it and owns the files, so nothing here touches the disk.</summary>
public interface IRepositoryService
{
	/// <summary>Gets the open project's repository.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The repository, or <see langword="null"/> while the project's import has not connected it yet.</returns>
	Task<Repository?> GetAsync(CancellationToken cancellationToken);

	/// <summary>Connects the open project's repository, as the last step of its import.</summary>
	/// <param name="input">The repository fields.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>
	/// The connected repository, or a failure describing why the input was rejected, including when the project already has one. When two callers connect at once, exactly one succeeds.
	/// </returns>
	Task<ServiceResult<Repository>> ConnectAsync(RepositoryInput input, CancellationToken cancellationToken);
}
