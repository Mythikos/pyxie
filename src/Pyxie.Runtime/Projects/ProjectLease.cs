using Pyxie.Data.Entities;
using Pyxie.Runtime.Orchestration;

namespace Pyxie.Runtime.Projects;

/// <summary>Keeps project-scoped operations on the same open project until their last service call finishes.</summary>
public sealed class ProjectLease : IDisposable
{
	private PipelineController? _controller;

	internal ProjectLease(Project project, PipelineController controller)
	{
		Project = project;
		_controller = controller;
	}

	/// <summary>Gets the project held by this operation.</summary>
	public Project Project { get; }

	/// <inheritdoc/>
	public void Dispose()
	{
		Interlocked.Exchange(ref _controller, null)?.EndWork();
	}
}
