using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Git;

/// <summary>A commit names the card it landed only through its <c>Pyxie-Card</c> trailer: exactly one, a whole number of at least 1, in the message's last paragraph, which is where git reads trailers. Nothing else in a commit, its subject included, names a card.</summary>
public sealed class GitClientCardTrailerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("Plain commit", null)]
	[InlineData("Merge pyxie/card-5: Looks like a merge Pyxie wrote", null)]
	[InlineData("Merge pyxie/card-5: Card five\n\nPyxie-Card: 5", 5)]
	[InlineData("Subject\n\nBody paragraph.\n\nPyxie-Card: 12", 12)]
	[InlineData("Subject\n\nPyxie-Card: 12\nSigned-off-by: A Person <person@example.test>", 12)]
	[InlineData("Subject\n\nPyxie-Card: 5\nPyxie-Card: 6", null)]
	[InlineData("Subject\n\nPyxie-Card: 5\nPyxie-Card: 5", null)]
	[InlineData("Subject\n\nPyxie-Card: five", null)]
	[InlineData("Subject\n\nPyxie-Card: 0", null)]
	[InlineData("Subject\n\nPyxie-Card: -3", null)]
	[InlineData("Subject\n\nPyxie-Card: 5.5", null)]
	[InlineData("Subject\n\nPyxie-Card: 99999999999999999999", null)]
	[InlineData("Subject\n\nPyxie-Card: 5\n\nA closing paragraph, so the trailer is not last.", null)]
	[InlineData("Subject\n\nSigned-off-by: A Person <person@example.test>", null)]
	public async Task LogAsync_ReadsTheCardFromTheTrailerAndNothingElse(string message, int? expected)
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("a.txt", "a", message);

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main", 0, 1, Token);

		Assert.Equal(expected, Assert.Single(commits).CardId);
	}

	[Fact]
	public async Task LogAsync_GivesEveryCommitItsOwnCard()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.CommitFileAsync("one.txt", "1", "Merge pyxie/card-1: One\n\nPyxie-Card: 1");
		await repository.CommitFileAsync("plain.txt", "p", "A person's commit");
		await repository.CommitFileAsync("two.txt", "2", "Merge pyxie/card-2: Two\n\nPyxie-Card: 2");

		IReadOnlyList<GitCommit> commits = await repository.Git.LogAsync(repository.Folder, "main", 0, 10, Token);

		Assert.Equal([2, null, 1, null], commits.Select(commit => commit.CardId));
		Assert.Equal(["Merge pyxie/card-2: Two", "A person's commit", "Merge pyxie/card-1: One", "Initial commit"], commits.Select(commit => commit.Subject));
	}

	[Fact]
	public async Task LogFirstParentAsync_ReadsTheCardFromTheTrailerToo()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string start = await repository.GetHashAsync();
		await repository.CommitFileAsync("one.txt", "1", "Merge pyxie/card-3: Three\n\nPyxie-Card: 3");
		await repository.CommitFileAsync("plain.txt", "p", "Merge pyxie/card-4: Not written by Pyxie");

		IReadOnlyList<GitCommit> commits = await repository.Git.LogFirstParentAsync(repository.Folder, $"{start}..main", 10, Token);

		Assert.Equal([null, 3], commits.Select(commit => commit.CardId));
	}

	[Fact]
	public async Task GetCommitAsync_ReadsTheCardFromTheTrailerAndKeepsTheFullMessage()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("one.txt", "1", "Merge pyxie/card-9: Nine\n\nPyxie-Card: 9");

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, tip, 1_000_000, Token);

		Assert.NotNull(detail);
		Assert.Equal(9, detail.Commit.CardId);
		Assert.Equal("Merge pyxie/card-9: Nine", detail.Commit.Subject);
		Assert.Equal("Merge pyxie/card-9: Nine\n\nPyxie-Card: 9", detail.Message.ReplaceLineEndings("\n"));
	}

	[Fact]
	public async Task GetCommitAsync_ForACommitWithoutTheTrailer_HasNoCard()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string tip = await repository.CommitFileAsync("one.txt", "1", "A person's commit\n\nIts body.");

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, tip, 1_000_000, Token);

		Assert.NotNull(detail);
		Assert.Null(detail.Commit.CardId);
		Assert.Equal("A person's commit\n\nIts body.", detail.Message.ReplaceLineEndings("\n"));
	}
}
