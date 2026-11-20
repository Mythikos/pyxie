using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>The list of projects in the machine database, and which one is open. Opening a project closes the one that is open in the same transaction, so at most one project is ever open, and after a committed open or close <see cref="OpenProject"/> follows. The catalog records projects only; the runtime creates, checks, and deletes their folders.</summary>
public interface IProjectCatalog
{
	/// <summary>Lists the projects.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The projects, ordered by name.</returns>
	Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken);

	/// <summary>Gets the project the machine database records as open.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The open project, or <see langword="null"/> when none is open.</returns>
	Task<Project?> GetOpenAsync(CancellationToken cancellationToken);

	/// <summary>Makes <see cref="OpenProject"/> match the machine database without writing to it: the project it records as open, or none. Startup calls it once, after it checked the open project's folder and brought its database up to date.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The open project, or <see langword="null"/> when none is open.</returns>
	Task<Project?> RestoreOpenAsync(CancellationToken cancellationToken);

	/// <summary>Adds a project, closed.</summary>
	/// <param name="name">The display name, which is also the folder's name.</param>
	/// <param name="folder">The full path of the project's folder.</param>
	/// <param name="source">The folder or URL the project's repository was imported from, which the project list shows. The caller removes any credentials from it first: the machine database never holds them.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The added project, or a failure when a field is invalid or another project already has the name or the folder (ignoring case).</returns>
	Task<ServiceResult<Project>> AddAsync(string name, string folder, string source, CancellationToken cancellationToken);

	/// <summary>Opens a project. Another project that is open is closed in the same transaction, so at most one project is ever open and there is never a moment with neither the old nor the new one open. Opening the project that is already open succeeds and closes nothing.</summary>
	/// <param name="id">The project's identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The open project, or a failure when it does not exist, in which case the open project stays open.</returns>
	Task<ServiceResult<Project>> OpenAsync(int id, CancellationToken cancellationToken);

	/// <summary>Closes a project, but only while it is the open one, in one conditional write. A caller whose list is out of date, or another tab, therefore cannot close a project that someone else opened since: that is refused, and nothing changes, <see cref="OpenProject"/> included.</summary>
	/// <param name="id">The identifier of the project to close.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the project does not exist or is not open.</returns>
	Task<ServiceResult> CloseAsync(int id, CancellationToken cancellationToken);

	/// <summary>Removes a closed project from the list: an import that failed undoes itself with it, and removing a project ends with it, once the runtime has deleted the project's folder and sandboxes. The folder is left alone here.</summary>
	/// <param name="id">The project's identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the project does not exist or is open.</returns>
	Task<ServiceResult> RemoveAsync(int id, CancellationToken cancellationToken);
}
