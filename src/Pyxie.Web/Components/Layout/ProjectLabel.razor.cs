using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;

namespace Pyxie.Web.Components.Layout;

/// <summary>The open project's name in the header, with the branch Pyxie merges finished cards into. The header renders it inside <see cref="Projects.OpenProjectView"/>, which creates it again for each project, so it reads the repository once.</summary>
public partial class ProjectLabel : ComponentBase, IDisposable
{
	private readonly CancellationTokenSource _cts = new();
	private string? _targetBranch;

	/// <summary>Gets or sets the open project.</summary>
	[Parameter]
	[EditorRequired]
	public required Project Project { get; set; }

	/// <summary>Gets or sets the open project's repository record, supplied by the framework.</summary>
	[Inject]
	public required IRepositoryService Repositories { get; set; }

	/// <inheritdoc/>
	public void Dispose()
	{
		_cts.Cancel();
		_cts.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnInitializedAsync()
	{
		try
		{
			_targetBranch = (await Repositories.GetAsync(_cts.Token))?.TargetBranch;
		}
		catch (OperationCanceledException) when (_cts.IsCancellationRequested)
		{
			// The header moved on to another project, or the circuit closed, before the read finished.
		}
	}
}
