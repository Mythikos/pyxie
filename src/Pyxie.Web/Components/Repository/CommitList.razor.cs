using System.Globalization;

using Microsoft.AspNetCore.Components;

using Pyxie.Runtime.Git;
using Pyxie.Runtime.Repositories;
using Pyxie.Web.Components.Display;
using Pyxie.Web.Components.Repository.Loading;
using Pyxie.Web.Components.Repository.Navigation;

using RepositoryEntity = Pyxie.Data.Entities.Repository;

namespace Pyxie.Web.Components.Repository;

/// <summary>The History tab: one page of the selected branch's first-parent history, newest first, with paging, so a card shows as the merge that landed it rather than as the commits inside it. Pyxie's merge commits carry her mark, and those that landed a card name it; every other commit carries its author's initial.</summary>
public partial class CommitList : ComponentBase, IDisposable
{
	private const string IsoPattern = "yyyy-MM-dd'T'HH:mm:ss'Z'";

	private readonly LatestLoad _load = new();
	private IReadOnlyList<GitCommit>? _commits;
	private string? _error;
	private string? _loadedKey;

	/// <summary>Gets or sets the repository to read.</summary>
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

	/// <summary>Gets or sets the clock the relative times are measured against, supplied by the framework.</summary>
	[Inject]
	public required TimeProvider Time { get; set; }

	// A full page may be the last one, so Older stays available and an empty page says so.
	private bool HasOlder => _commits is not null && _commits.Count == RepositoryBrowser.CommitsPerPage;

	/// <inheritdoc/>
	public void Dispose()
	{
		_load.Dispose();
	}

	/// <inheritdoc/>
	protected override async Task OnParametersSetAsync()
	{
		string tip = Overview.Branches.FirstOrDefault(branch => branch.Name == Location.Branch)?.Sha ?? string.Empty;
		string key = $"{Overview.Repository.Path}|{Location.Branch}|{tip}|{Location.Page}";

		if (key == _loadedKey)
		{
			return;
		}

		_loadedKey = key;
		await LoadAsync();
	}

	private static string IsoOf(GitCommit commit)
	{
		return DateTime.SpecifyKind(commit.AuthoredUtc, DateTimeKind.Utc).ToString(IsoPattern, CultureInfo.InvariantCulture);
	}

	private string AgoOf(GitCommit commit)
	{
		return RelativeTime.Describe(commit.AuthoredUtc, Time.GetUtcNow().UtcDateTime, Time.LocalTimeZone);
	}

	private string Href(GitCommit commit)
	{
		return Navigation.RepositoryUri(Location with { Commit = commit.Sha });
	}

	private Task Newer()
	{
		Navigation.NavigateTo(Navigation.RepositoryUri(Location with { Page = Location.Page - 1 }));

		return Task.CompletedTask;
	}

	private Task Older()
	{
		Navigation.NavigateTo(Navigation.RepositoryUri(Location with { Page = Location.Page + 1 }));

		return Task.CompletedTask;
	}

	private Task LoadAsync()
	{
		RepositoryEntity repository = Overview.Repository;
		string revision = BranchSelection.Revision(Location.Branch);
		int page = Location.Page;

		return _load.RunAsync(
			token => Browser.GetHistoryAsync(repository, revision, page, token),
			commits =>
			{
				_commits = commits;
				_error = null;
			},
			message =>
			{
				_commits = null;
				_error = message;
			});
	}
}
