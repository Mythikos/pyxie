using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

using Pyxie.Runtime.Git;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryCommitListTests
{
	private const string MergeMessage = "Merge pyxie/card-48: Sandbox firewall refuses the host\n\nPyxie-Card: 48";

	// The clock of the host stands at 2030-01-01T00:00:00Z.
	private static readonly DateTimeOffset s_threeHoursAgo = new(2029, 12, 31, 21, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset s_twoHoursAgo = new(2029, 12, 31, 22, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset s_fiveMinutesAgo = new(2029, 12, 31, 23, 55, 0, TimeSpan.Zero);

	[Fact]
	public async Task Render_GivesPyxiesMergeCommitsHerMarkAndACardChipAndEveryOtherCommitItsAuthorsInitial()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("pyxie/card-48");
		await source.CheckoutAsync("pyxie/card-48");
		await DatedCommits.CommitFileAtAsync(source, "firewall.txt", "firewall", s_threeHoursAgo, "Refuse the host", identity: PyxieIdentity.Environment);
		await source.CheckoutAsync(TempGitRepository.MainBranch);
		await DatedCommits.CommitFileAtAsync(source, "a.txt", "a", s_twoHoursAgo, "Tighten AllowedHosts defaults");
		await DatedCommits.MergeAtAsync(source, "pyxie/card-48", s_fiveMinutesAgo, MergeMessage, identity: PyxieIdentity.Environment);
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		IReadOnlyList<string> rows = Rows(await host.List.HtmlAsync());

		Assert.Equal(["Merge pyxie/card-48: Sandbox firewall refuses the host", "Tighten AllowedHosts defaults", "Initial commit"], Subjects(rows));
		AssertPyxiesMark(rows[0]);
		AssertChip(rows[0], 48);

		AssertInitial(rows[1], "P");
		AssertChip(rows[1], null);
	}

	[Fact]
	public async Task Render_OnTheTargetBranch_ListsEachCardAsTheMergeThatLandedItAndNothingOfTheCommitsInsideIt()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await LandedCardsHistory.BuildAsync(source);
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		IReadOnlyList<string> rows = Rows(await host.List.HtmlAsync());

		Assert.Equal([LandedCardsHistory.Card1Landing, LandedCardsHistory.Card2Landing, "Initial commit"], Subjects(rows));
		AssertPyxiesMark(rows[0]);
		AssertAuthor(rows[0], PyxieIdentity.Name);
		AssertChip(rows[0], 1);

		AssertPyxiesMark(rows[1]);
		AssertAuthor(rows[1], PyxieIdentity.Name);
		AssertChip(rows[1], 2);

		AssertInitial(rows[2], "P");
		AssertAuthor(rows[2], TempGitRepository.AuthorName);
		AssertChip(rows[2], null);
	}

	[Fact]
	public async Task Render_OnACardsBranch_GivesPyxiesMergeOfTheTargetBranchHerMarkButNotTheAgentsCommitSignedWithHerName()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await LandedCardsHistory.BuildAsync(source);
		await using CommitListHost host = await CommitListHost.RenderAsync(source, branch: LandedCardsHistory.Card1Branch);

		IReadOnlyList<string> rows = Rows(await host.List.HtmlAsync());

		Assert.Equal([LandedCardsHistory.Card1CatchUp, LandedCardsHistory.Card1Work, "Initial commit"], Subjects(rows));
		AssertPyxiesMark(rows[0]);
		AssertAuthor(rows[0], PyxieIdentity.Name);
		AssertChip(rows[0], null);

		AssertInitial(rows[1], "P");
		AssertAuthor(rows[1], PyxieIdentity.Name);
		AssertChip(rows[1], null);

		AssertInitial(rows[2], "P");
		AssertAuthor(rows[2], TempGitRepository.AuthorName);
		AssertChip(rows[2], null);
	}

	[Theory]
	[InlineData("Merge feature: Add the notes", null)]
	[InlineData("Merge feature: Add the notes\n\nPyxie-Card: 3", 3)]
	public async Task Render_GivesAPersonsMergeCommitTheirInitialAndNotPyxiesMark(string message, int? card)
	{
		IReadOnlyDictionary<string, string> ada = DatedCommits.IdentityOf("Ada Lovelace", "ada@example.test");
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("feature");
		await source.CheckoutAsync("feature");
		await DatedCommits.CommitFileAtAsync(source, "notes.txt", "notes", s_twoHoursAgo, "Add the notes", identity: ada);
		await source.CheckoutAsync(TempGitRepository.MainBranch);
		await DatedCommits.MergeAtAsync(source, "feature", s_fiveMinutesAgo, message, identity: ada);
		await using CommitListHost host = await CommitListHost.RenderAsync(source);

		IReadOnlyList<string> rows = Rows(await host.List.HtmlAsync());

		Assert.Equal(["Merge feature: Add the notes", "Initial commit"], Subjects(rows));
		AssertInitial(rows[0], "A");
		AssertAuthor(rows[0], "Ada Lovelace");
		AssertChip(rows[0], card);
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

	// The text of each row's subject.
	private static IReadOnlyList<string> Subjects(IReadOnlyList<string> rows)
	{
		return [.. rows.Select(row => WebUtility.HtmlDecode(Regex.Match(row, "<a class=\"commit-subject\"[^>]*>(.*?)</a>", RegexOptions.Singleline, TimeSpan.FromSeconds(5)).Groups[1].Value))];
	}

	private static void AssertPyxiesMark(string row)
	{
		Assert.Contains("commit-avatar commit-avatar-pyxie", row, StringComparison.Ordinal);
		Assert.Matches(new Regex("<img class=\"pyxie-mark\"[^>]*src=\"brand/pyxie-mark-small.svg\"[^>]*width=\"22\"[^>]*height=\"22\"[^>]*alt=\"Pyxie\"", RegexOptions.None, TimeSpan.FromSeconds(5)), row);
	}

	// The author's initial, and not Pyxie's mark: an initial of P also stands for an agent's commit signed Pyxie and for the test person Pyxie Tests, so AssertAuthor tells those apart.
	private static void AssertInitial(string row, string initial)
	{
		Assert.Matches(new Regex($"<span class=\"commit-avatar\"[^>]*aria-hidden=\"true\"[^>]*>{initial}</span>", RegexOptions.None, TimeSpan.FromSeconds(5)), row);
		Assert.DoesNotContain("pyxie-mark", row, StringComparison.Ordinal);
	}

	private static void AssertAuthor(string row, string name)
	{
		Assert.Matches(new Regex($"<span class=\"commit-meta\"[^>]*>{Regex.Escape(name)} · <time", RegexOptions.None, TimeSpan.FromSeconds(5)), WebUtility.HtmlDecode(row));
	}

	// The green chip that names the card a commit landed, or none.
	private static void AssertChip(string row, int? card)
	{
		if (card is int number)
		{
			Assert.Matches(new Regex($"<a class=\"pyx-chip pyx-chip-green commit-card\" href=\"/\\?project=1&amp;card={number.ToString(CultureInfo.InvariantCulture)}\"[^>]*>Card {number.ToString(CultureInfo.InvariantCulture)}</a>", RegexOptions.None, TimeSpan.FromSeconds(5)), row);
		}
		else
		{
			Assert.DoesNotContain("pyx-chip", row, StringComparison.Ordinal);
		}
	}

	// The markup of each row of the list, in order.
	private static IReadOnlyList<string> Rows(string html)
	{
		return [.. Regex.Matches(html, "<li class=\"commit-row\".*?</li>", RegexOptions.Singleline, TimeSpan.FromSeconds(5)).Select(match => match.Value)];
	}
}
