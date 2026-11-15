using Pyxie.Runtime.Git;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Repository.Diffs;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryDiffParserGitTests
{
	private const int MaxPatchLength = 400_000;

	[Fact]
	public async Task Parse_CountsTheSameAddedAndRemovedLinesAsGitForEveryKindOfChange()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await repository.WriteFileAsync("notes.txt", "alpha\n-- dashes\nbeta\n++ pluses\ngamma\n");
		await repository.WriteFileAsync("no-newline.txt", "first\nlast");
		await repository.WriteFileAsync("moved-from.txt", "one\ntwo\nthree\nfour\nfive\nsix\n");
		await repository.WriteFileAsync("untouched.txt", "same\n");
		await repository.CommitAsync("Add files");

		await repository.WriteFileAsync("notes.txt", "alpha\nbeta changed\n++ pluses\ngamma\n++ new pluses\n");
		await repository.WriteFileAsync("no-newline.txt", "first\nlast changed");
		await repository.RunGitAsync("mv", "moved-from.txt", "moved-to.txt");
		await repository.WriteFileAsync("fresh.txt", "fresh one\nfresh two\n");
		await repository.WriteFileAsync("picture.bin", "binary\0content\0here");
		string commit = await repository.CommitAsync("Change many things");

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, commit, MaxPatchLength, TestContext.Current.CancellationToken);
		Assert.NotNull(detail);
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(detail.Patch);

		int added = CountLines(blocks, DiffBlockKind.Added);
		int removed = CountLines(blocks, DiffBlockKind.Removed);
		int gitAdded = detail.Files.Sum(file => file.Additions ?? 0);
		int gitRemoved = detail.Files.Sum(file => file.Deletions ?? 0);
		int fileHeaders = blocks.Count(block => block.Kind == DiffBlockKind.Header && block.Text.StartsWith("diff --git ", StringComparison.Ordinal));

		Assert.True(gitAdded > 0 && gitRemoved > 0, $"The commit should add and remove lines, but git counted +{gitAdded} -{gitRemoved}.");
		Assert.True(added == gitAdded, $"The parser found {added} added lines but git counted {gitAdded}.");
		Assert.True(removed == gitRemoved, $"The parser found {removed} removed lines but git counted {gitRemoved}.");
		Assert.True(fileHeaders == detail.Files.Count, $"The parser found {fileHeaders} files but git changed {detail.Files.Count}.");
		Assert.Contains(blocks, block => block.Kind == DiffBlockKind.Removed && block.Text.Contains("--- dashes", StringComparison.Ordinal));
		Assert.Contains(blocks, block => block.Kind == DiffBlockKind.Note && block.Text.StartsWith("\\ No newline at end of file", StringComparison.Ordinal));
		Assert.Contains(blocks, block => block.Kind == DiffBlockKind.Header && block.Text.Contains("Binary files", StringComparison.Ordinal));
		Assert.Contains(blocks, block => block.Kind == DiffBlockKind.Header && block.Text.Contains("rename from moved-from.txt", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Parse_ReadsTheDiffOfTheRootCommit()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string root = await repository.GetHashAsync();

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, root, MaxPatchLength, TestContext.Current.CancellationToken);
		Assert.NotNull(detail);
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(detail.Patch);

		Assert.Equal([DiffBlockKind.Header, DiffBlockKind.Hunk, DiffBlockKind.Added, DiffBlockKind.Note], blocks.Select(block => block.Kind));
		Assert.Equal($"+{TempGitRepository.InitialContent}", blocks[2].Text);
	}

	[Fact]
	public async Task Parse_NotesWhereAVeryLongDiffWasCut()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		string content = string.Join('\n', Enumerable.Range(1, 400).Select(line => $"line {line} of a file that is long enough to need cutting"));
		string commit = await repository.CommitFileAsync("long.txt", content);

		GitCommitDetail? detail = await repository.Git.GetCommitAsync(repository.Folder, commit, 3_000, TestContext.Current.CancellationToken);
		Assert.NotNull(detail);
		IReadOnlyList<DiffBlock> blocks = DiffParser.Parse(detail.Patch);

		Assert.Contains(blocks, block => block.Kind == DiffBlockKind.Note && block.Text.StartsWith("[truncated: ", StringComparison.Ordinal));
		Assert.Equal(DiffBlockKind.Header, blocks[0].Kind);
		Assert.True(blocks.Count(block => block.Kind == DiffBlockKind.Header) == 1, "A cut diff should still have exactly one file header.");
		Assert.DoesNotContain(blocks, block => block.Kind == DiffBlockKind.Header && block.Text.Contains("line ", StringComparison.Ordinal));
	}

	private static int CountLines(IReadOnlyList<DiffBlock> blocks, DiffBlockKind kind)
	{
		return blocks.Where(block => block.Kind == kind).Sum(block => block.Text.Split('\n').Length);
	}
}
