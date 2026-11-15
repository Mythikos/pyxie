using Microsoft.Extensions.DependencyInjection;

using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Repositories;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Repository;
using Pyxie.Web.Components.Repository.Navigation;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The history list of a real git repository a test made, rendered on a clock the test controls, so "2 hours ago" is not a guess. The repository is connected as the open project's hub, with its first page of history on the target branch.</summary>
internal sealed class CommitListHost : IAsyncDisposable
{
	private readonly RepositoryRig _rig;
	private readonly ComponentHarness _harness;

	private CommitListHost(RepositoryRig rig, ComponentHarness harness, RenderedComponent<CommitList> list, TestClock clock)
	{
		_rig = rig;
		_harness = harness;
		List = list;
		Clock = clock;
	}

	/// <summary>Gets the rendered list.</summary>
	public RenderedComponent<CommitList> List { get; }

	/// <summary>Gets the clock the relative times are measured against.</summary>
	public TestClock Clock { get; }

	/// <summary>Renders the history of a repository's target branch.</summary>
	/// <param name="source">The repository, which becomes the hub.</param>
	/// <param name="page">The zero-based page of history.</param>
	/// <param name="clockOffset">How far past 2030-01-01T00:00:00Z the clock stands when the list renders; none unless given.</param>
	/// <returns>The rendered list.</returns>
	public static async Task<CommitListHost> RenderAsync(TempGitRepository source, int page = 0, TimeSpan? clockOffset = null)
	{
		RepositoryRig rig = new();
		await rig.ConnectAsync(source.Folder);
		RepositoryOverview overview = await rig.Browser.GetOverviewAsync(TestContext.Current.CancellationToken) ?? throw new InvalidOperationException("The repository was not connected.");
		TestClock clock = new();
		if (clockOffset is TimeSpan offset)
		{
			clock.Advance(offset);
		}

		ComponentHarness harness = new(services =>
		{
			services.AddSingleton(rig.Browser);
			services.AddSingleton<TimeProvider>(clock);
		});

		RenderedComponent<CommitList> list = await harness.RenderAsync<CommitList>(new Dictionary<string, object>
		{
			[nameof(CommitList.Overview)] = overview,
			[nameof(CommitList.Location)] = new RepositoryLocation(RepositoryTab.History, TempGitRepository.MainBranch, string.Empty, string.Empty, page),
		});

		return new CommitListHost(rig, harness, list, clock);
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _harness.DisposeAsync();
		await _rig.DisposeAsync();
	}
}
