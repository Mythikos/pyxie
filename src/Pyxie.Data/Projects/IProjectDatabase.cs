namespace Pyxie.Data.Projects;

/// <summary>Creates and upgrades the database in a project's folder. The provider assembly implements it.</summary>
public interface IProjectDatabase
{
	/// <summary>Applies every pending migration to the database in a project's folder, creating the database (and the folder) when it does not exist.</summary>
	/// <param name="folder">The project's folder.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A task that completes when the database is up to date.</returns>
	Task MigrateAsync(string folder, CancellationToken cancellationToken);
}
