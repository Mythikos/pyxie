using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

public sealed class GitClientLogTests
{
	private const int CommitCount = 6;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(0, 3, new[] { "Commit 6", "Commit 5", "Commit 4" })]
	[InlineData(3, 3, new[] { "Commit 3", "Commit 2", "Commit 1" })]
	[InlineData(6, 3, new[] { "Initial commit" })]
	[InlineData(2, 2, new[] { "Commit 4", "Commit 3" })]
	[InlineData(7, 3, new string[0])]
	[InlineData(0, 100, new[] { "Commit 6", "Commit 5", "Commit 4", "Commit 3", "Commit 2", "Commit 1", "Initial commit" })]
	public async Task LogAsync_PagesNewestFirstWithSkipAndTake(int skip, int take, string[] expectedSubjects)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await EmptyCommits.AddAsync(repository, "main", CommitCount);

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main", skip, take, Token);

		Assert.Equal(expectedSubjects, commits.Select(commit => commit.Subject));
	}

	[Fact]
	public async Task LogAsync_DescribesEachCommitWithItsHashesParentAuthorAndDate()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string initial = await repository.GetHashAsync();
		await EmptyCommits.AddAsync(repository, "main", 2);
		string first = await repository.GetHashAsync("main~1");
		string second = await repository.GetHashAsync("main");

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main", 0, 10, Token);

		Assert.Equal([second, first, initial], commits.Select(commit => commit.Sha));
		Assert.Equal([first, initial, string.Empty], commits.Select(commit => string.Join(',', commit.ParentShas)));
		Assert.All(commits, commit => Assert.StartsWith(commit.ShortSha, commit.Sha, StringComparison.Ordinal));
		Assert.All(commits, commit => Assert.True(commit.ShortSha.Length >= 7, $"The short hash {commit.ShortSha} should be at least seven characters."));
		Assert.All(commits, commit => Assert.Equal((TempGitRepository.AuthorName, "tests@pyxie.invalid"), (commit.AuthorName, commit.AuthorEmail)));
		Assert.Equal(EmptyCommits.FirstCommitTime.AddMinutes(1).UtcDateTime, commits[0].AuthoredUtc);
		Assert.Equal(EmptyCommits.FirstCommitTime.UtcDateTime, commits[1].AuthoredUtc);
		Assert.All(commits, commit => Assert.Equal(DateTimeKind.Utc, commit.AuthoredUtc.Kind));
	}

	[Fact]
	public async Task LogAsync_ReportsOnlyTheFirstLineOfTheMessageAsTheSubject()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("a.txt", "a", "Subject line\n\nBody paragraph.\nSecond body line.");

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main", 0, 1, Token);

		Assert.Equal("Subject line", Assert.Single(commits).Subject);
	}

	[Fact]
	public async Task LogAsync_ForARange_ListsOnlyTheCommitsInIt()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		Divergence divergence = await Divergence.CleanAsync(repository, "feature");
		await repository.CheckoutAsync("feature");
		string second = await repository.CommitFileAsync("feature-second.txt", "two");

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main..feature", 0, 10, Token);

		Assert.Equal([second, divergence.FeatureTip], commits.Select(commit => commit.Sha));
	}

	[Theory]
	[InlineData("no-such-branch")]
	[InlineData("--all")]
	[InlineData("--help")]
	public async Task LogAsync_ForARevisionThatDoesNotExist_ReturnsNoCommitsInsteadOfTreatingItAsAnOption(string revision)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, revision, 0, 10, Token);

		Assert.Empty(commits);
	}
}
