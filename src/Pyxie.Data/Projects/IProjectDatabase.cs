using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Projects;

/// <summary>Creates and upgrades the database in a project's folder. The provider assembly implements it.</summary>
public interface IProjectDatabase
{
	/// <summary>Applies every pending migration to the database in a project's folder, creating the database (and the folder) when it does not exist.</summary>
	/// <param name="folder">The project's folder.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>Success when the database is up to date, or an actionable refusal when it belongs to another build or cannot be read or updated. Cancellation still throws.</returns>
	Task<ServiceResult> MigrateAsync(string folder, CancellationToken cancellationToken);
}
