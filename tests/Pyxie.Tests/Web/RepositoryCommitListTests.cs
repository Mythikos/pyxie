using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryCommitListTests
{
	private const string MergeMessage = "Merge pyxie/card-48: Sandbox firewall refuses the host\n\nPyxie-Card: 48";

	// The clock of the host stands at 2030-01-01T00:00:00Z.
	private static readonly DateTimeOffset s_twoHoursAgo = new(2029, 12, 31, 22, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset s_fiveMinutesAgo = new(2029, 12, 31, 23, 55, 0, TimeSpan.Zero);

	[Fact]
	public async Task Render_GivesPyxiesMergeCommitsHerMarkAndACardChipAndEveryOtherCommitItsAuthorsInitial()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await DatedCommits.CommitFileAtAsync(source, "a.txt", "a", s_twoHoursAgo, "Tighten AllowedHosts defaults");
		await DatedCommits.CommitFileAtAsync(source, "b.txt", "b", s_fiveMinutesAgo, MergeMessage);
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		IReadOnlyList<string> rows = Rows(await host.List.HtmlAsync());

		Assert.Equal(3, rows.Count);
		string merge = rows[0];
		Assert.Contains("commit-avatar commit-avatar-pyxie", merge, StringComparison.Ordinal);
		Assert.Matches(new Regex("<img class=\"pyxie-mark\"[^>]*src=\"brand/pyxie-mark-small.svg\"[^>]*width=\"22\"[^>]*height=\"22\"[^>]*alt=\"Pyxie\"", RegexOptions.None, TimeSpan.FromSeconds(5)), merge);
		Assert.Matches(new Regex("<span class=\"pyx-chip pyx-chip-green commit-card\"[^>]*>Card 48</span>", RegexOptions.None, TimeSpan.FromSeconds(5)), merge);

		string person = rows[1];
		Assert.Matches(new Regex("<span class=\"commit-avatar\"[^>]*aria-hidden=\"true\"[^>]*>P</span>", RegexOptions.None, TimeSpan.FromSeconds(5)), person);
		Assert.DoesNotContain("pyxie-mark", person, StringComparison.Ordinal);
		Assert.DoesNotContain("pyx-chip", person, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Render_ShowsTheSubjectAsALinkToTheCommitTheAuthorWithHowLongAgoAndTheShortHashOnTheRight()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		string tip = await DatedCommits.CommitFileAtAsync(source, "a.txt", "a", s_twoHoursAgo, "Tighten AllowedHosts defaults");
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		string row = Rows(await host.List.HtmlAsync())[0];

		Assert.Matches(new Regex($"<a class=\"commit-subject\"[^>]*href=\"[^\"]*commit={tip}[^\"]*\"[^>]*>Tighten AllowedHosts defaults</a>", RegexOptions.None, TimeSpan.FromSeconds(5)), WebUtility.HtmlDecode(row));
		Assert.Matches(new Regex($"<span class=\"commit-meta\"[^>]*>{TempGitRepository.AuthorName} · <time datetime=\"2029-12-31T22:00:00Z\" title=\"[^\"]+\"[^>]*>2 hours ago</time></span>", RegexOptions.None, TimeSpan.FromSeconds(5)), WebUtility.HtmlDecode(row));
		Assert.Matches(new Regex($"<code class=\"commit-sha\"[^>]*>{tip[..7]}</code>", RegexOptions.None, TimeSpan.FromSeconds(5)), row);
		Assert.True(row.IndexOf("commit-subject", StringComparison.Ordinal) < row.IndexOf("commit-meta", StringComparison.Ordinal) && row.IndexOf("commit-meta", StringComparison.Ordinal) < row.IndexOf("commit-sha", StringComparison.Ordinal), "The subject comes first, then the author and time, then the hash.");
	}

	[Fact]
	public async Task Render_SaysHowLongAgoFromTheClockItIsGivenAndKeepsTheExactTimeAsATooltip()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await DatedCommits.CommitFileAtAsync(source, "a.txt", "a", s_fiveMinutesAgo, "Fresh");
		await using CommitListHost host = await CommitListHost.RenderAsync(source, clockOffset: TimeSpan.FromMinutes(10));

		string row = Rows(await host.List.HtmlAsync())[0];

		Assert.Matches(new Regex("<time datetime=\"2029-12-31T23:55:00Z\" title=\"[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}\"[^>]*>15 minutes ago</time>", RegexOptions.None, TimeSpan.FromSeconds(5)), row);
	}

	[Fact]
	public async Task Render_OffersNewerAsUnavailableOnTheFirstPage()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		string html = await host.List.HtmlAsync();

		Assert.Matches(new Regex("<button[^>]*\\bdisabled\\b[^>]*>Newer</button>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
		Assert.Matches(new Regex("<button[^>]*\\bdisabled\\b[^>]*>Older</button>", RegexOptions.None, TimeSpan.FromSeconds(5)), html);
	}

	// The markup of each row of the list, in order.
	private static IReadOnlyList<string> Rows(string html)
	{
		return [.. Regex.Matches(html, "<li class=\"commit-row\".*?</li>", RegexOptions.Singleline, TimeSpan.FromSeconds(5)).Select(match => match.Value)];
	}
}
