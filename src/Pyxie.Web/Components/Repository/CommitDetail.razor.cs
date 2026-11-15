using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Repository.Loading;
using Pyxie.Web.Components.Repository.Navigation;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>The History tab for one commit: its message, author, date, parents, changed files, and diff.</summary>
public partial class CommitDetail : ComponentBase, IDisposable
{
	private readonly LatestLoad _load = new();
	private GitCommitDetail? _detail;
	private bool _missing;
	private string? _error;
	private string? _loadedKey;

	/// <summary>Gets or sets the repository to read.</summary>
	[Parameter]
	public required RepositoryOverview Overview { get; set; }

	/// <summary>Gets or sets the location to show, which names the commit.</summary>
	[Parameter]
	public required RepositoryLocation Location { get; set; }

	/// <summary>Gets or sets the repository browser, supplied by the framework.</summary>
	[Inject]
	public required RepositoryBrowser Browser { get; set; }

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	private string BackHref => Navigation.RepositoryUri(Location with { Commit = string.Empty });

	/// <inheritdoc/>
	public void Dispose()
	{
		_load.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnParametersSetAsync()
	{
		string key = $"{Overview.Repository.Path}|{Location.Commit}";

		if (key == _loadedKey)
		{
			return;
		}

		_loadedKey = key;
		await LoadAsync();
	}

	private string ParentHref(string sha)
	{
		return Navigation.RepositoryUri(Location with { Commit = sha });
	}

	private Task LoadAsync()
	{
		RepositoryEntity repository = Overview.Repository;
		string sha = Location.Commit;

		return _load.RunAsync(
			token => Browser.GetCommitAsync(repository, sha, token),
			detail =>
			{
				_detail = detail;
				_missing = detail is null;
				_error = null;
			},
			message =>
			{
				_detail = null;
				_missing = false;
				_error = message;
			});
	}
}
