using Blazorise;

using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Repository.Display;
using Pyxie.Web.Components.Repository.Loading;
using Pyxie.Web.Components.Repository.Navigation;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>The Files tab: the folder or file the location names on the branch the page shows, under a breadcrumb. The branch itself is chosen in the tab bar.</summary>
public partial class FileBrowser : ComponentBase, IDisposable
{
	private readonly LatestLoad _load = new();
	private IReadOnlyList<GitTreeEntry>? _entries;
	private GitFileContent? _file;
	private bool _notFound;
	private string? _error;
	private string? _loadedKey;

	/// <summary>Gets or sets the repository to browse.</summary>
	[Parameter]
	public required RepositoryOverview Overview { get; set; }

	/// <summary>Gets or sets the location to show. Its branch is one that exists.</summary>
	[Parameter]
	public required RepositoryLocation Location { get; set; }

	/// <summary>Gets or sets the repository browser, supplied by the framework.</summary>
	[Inject]
	public required RepositoryBrowser Browser { get; set; }

	/// <summary>Gets or sets the navigation manager, supplied by the framework.</summary>
	[Inject]
	public required NavigationManager Navigation { get; set; }

	private IReadOnlyList<BreadcrumbSegment> Trail => BreadcrumbTrail.Of(Location.Path);

	private string NotFoundText => Location.Path.Length == 0 ? $"The branch {Location.Branch} has no files." : $"{Location.Path} does not exist on {Location.Branch}.";

	/// <inheritdoc/>
	public void Dispose()
	{
		_load.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnParametersSetAsync()
	{
		// The parent renders this component again for every change it makes, so it reads git only when what is shown changed: the folder, the branch, or the commit the branch points at.
		string tip = Overview.Branches.FirstOrDefault(branch => branch.Name == Location.Branch)?.Sha ?? string.Empty;
		string key = $"{Overview.Repository.Path}|{Location.Branch}|{tip}|{Location.Path}";

		if (key == _loadedKey)
		{
			return;
		}

		_loadedKey = key;
		await LoadAsync();
	}

	private static IconName IconFor(GitTreeEntry entry) => entry.Kind == GitTreeEntryKind.Directory ? IconName.Folder : IconName.File;

	private string Href(string path)
	{
		return Navigation.RepositoryUri(Location with { Path = path });
	}

	// One address can name a folder or a file, so the path is read as a folder first and as a file when it is not one.
	private Task LoadAsync()
	{
		RepositoryEntity repository = Overview.Repository;
		string revision = BranchSelection.Revision(Location.Branch);
		string path = Location.Path;

		return _load.RunAsync(
			async token =>
			{
				IReadOnlyList<GitTreeEntry>? entries = await Browser.ListAsync(repository, revision, path, token);
				GitFileContent? file = entries is null && path.Length > 0 ? await Browser.ReadAsync(repository, revision, path, token) : null;

				return (Entries: entries, File: file);
			},
			result =>
			{
				_entries = result.Entries;
				_file = result.File;
				_notFound = result.Entries is null && result.File is null;
				_error = null;
			},
			message =>
			{
				_entries = null;
				_file = null;
				_notFound = false;
				_error = message;
			});
	}
}
