using Pyxie.Data.Entities;
using Pyxie.Data.Services;

namespace Pyxie.Data.Projects;

/// <summary>The project that is open, as the machine database last committed it. Every project database operation runs against it. Only <see cref="IProjectCatalog"/> sets it, after its write commits; everyone else reads it. It is a singleton and safe to read from any thread.</summary>
public sealed class OpenProject
{
	private volatile Project? _current;

	/// <summary>Raised after the open project changed. Handlers run on the thread that changed it and must not throw.</summary>
	public event Action? Changed;

	/// <summary>Gets a snapshot of the open project, or <see langword="null"/> when none is open. The snapshot is never changed; a new one replaces it.</summary>
	public Project? Current => _current;

	/// <summary>Replaces the open project with a detached copy of <paramref name="project"/>, and announces it.</summary>
	/// <param name="project">The project that is now open, or <see langword="null"/> when none is.</param>
	internal void Set(Project? project)
	{
		_current = project is null
			? null
			: new Project { Id = project.Id, Name = project.Name, Folder = project.Folder, CreatedUtc = project.CreatedUtc, IsOpen = true };
		Changed?.Invoke();
	}
}
