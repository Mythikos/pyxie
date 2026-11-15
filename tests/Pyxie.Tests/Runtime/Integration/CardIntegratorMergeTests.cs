using System.Globalization;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorMergeTests
{
	[Fact]
	public async Task IntegrateAsync_ForACardThatContainsTheTarget_LandsATwoParentMergeCommitWithTheCardsTreeOnTheTarget()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		string mainTip = await rig.TipAsync("main");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		string mergeCommit = await rig.TipAsync("main");
		Assert.NotEqual(cardTip, mergeCommit);
		Assert.Equal($"{mainTip} {cardTip}", await rig.ParentsAsync(mergeCommit));
		Assert.Equal("Merge pyxie/card-1: Card 1", await rig.SubjectAsync(mergeCommit));
		Assert.Equal(await rig.TreeAsync(cardTip), await rig.TreeAsync(mergeCommit));
		Assert.Equal("the card's work", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal(cardTip, await rig.TipAsync("pyxie/card-1"));
	}

	[Fact]
	public async Task IntegrateAsync_WritesTheMergeCommitAsPyxieWhateverIdentityTheHubHasConfigured()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);

		// git takes an author or committer from author.* and committer.* before user.*, so these discriminate a commit made only with -c user.*.
		await rig.Hub.RunGitAsync("config", "author.name", "Configured author");
		await rig.Hub.RunGitAsync("config", "author.email", "author@configured.invalid");
		await rig.Hub.RunGitAsync("config", "committer.name", "Configured committer");
		await rig.Hub.RunGitAsync("config", "committer.email", "committer@configured.invalid");
		await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal(TempGitRepository.AuthorName, await rig.Hub.RunGitAsync("config", "user.name"));
		Assert.Equal("Pyxie|pyxie@localhost|Pyxie|pyxie@localhost", await rig.Hub.RunGitAsync("log", "-1", "--format=%an|%ae|%cn|%ce", "main"));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(42)]
	public async Task IntegrateAsync_GivesTheMergeCommitItsSubjectAndTheCardTrailerAsTheLastParagraph(int cardId)
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(cardId, "feature.txt", "the card's work");

		RoundVerdict verdict = await rig.IntegrateAsync(cardId);

		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		Assert.Equal($"Merge pyxie/card-{cardId}: Card {cardId}", await rig.SubjectAsync("main"));
		Assert.Equal($"Merge pyxie/card-{cardId}: Card {cardId}\n\nPyxie-Card: {cardId}", (await rig.Hub.RunGitAsync("log", "-1", "--format=%B", "main")).ReplaceLineEndings("\n"));
		Assert.Equal(cardId.ToString(CultureInfo.InvariantCulture), await rig.Hub.RunGitAsync("log", "-1", "--format=%(trailers:key=Pyxie-Card,valueonly)", "main"));
	}

	[Fact]
	public async Task IntegrateAsync_Succeeds_WithTheMergeCommitInTheNoteAndTheCardsCommitsInTheReport()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string first = await rig.CommitOnCardAsync(1, "first.txt", "one");
		string second = await rig.CommitOnCardAsync(1, "second.txt", "two");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		string mergeCommit = await rig.TipAsync("main");
		Assert.Equal($"Merged into main as {mergeCommit[..12]}.", verdict.Note);
		Assert.NotNull(verdict.Report);
		string[] lines = verdict.Report.ReplaceLineEndings("\n").Split('\n');
		Assert.Equal(4, lines.Length);
		Assert.Equal($"Merge commit {mergeCommit}", lines[0]);
		Assert.Equal(string.Empty, lines[1]);
		Assert.StartsWith($"- {second[..7]}", lines[2], StringComparison.Ordinal);
		Assert.EndsWith(" Add second.txt", lines[2], StringComparison.Ordinal);
		Assert.StartsWith($"- {first[..7]}", lines[3], StringComparison.Ordinal);
		Assert.EndsWith(" Add first.txt", lines[3], StringComparison.Ordinal);
	}

	[Fact]
	public async Task IntegrateAsync_IntoATargetBranchOtherThanMain_MovesThatBranchAndLeavesEveryOtherRefAlone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("release");
		await source.CheckoutAsync("release");
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		string releaseTip = await rig.TipAsync("release");
		IReadOnlyList<string> otherRefsBefore = OtherThanRelease(await rig.ListRefsAsync());

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal("release", rig.TargetBranch);
		Assert.Equal(RoundResult.Succeeded, verdict.Result);
		string mergeCommit = await rig.TipAsync("release");
		Assert.Equal($"Merged into release as {mergeCommit[..12]}.", verdict.Note);
		Assert.StartsWith($"Merge commit {mergeCommit}", verdict.Report, StringComparison.Ordinal);
		Assert.Equal($"{releaseTip} {cardTip}", await rig.ParentsAsync(mergeCommit));
		Assert.Equal("Merge pyxie/card-1: Card 1", await rig.SubjectAsync(mergeCommit));
		Assert.Equal(otherRefsBefore, OtherThanRelease(await rig.ListRefsAsync()));
		Assert.Null(await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
	}

	private static List<string> OtherThanRelease(IReadOnlyList<string> refs)
	{
		return [.. refs.Where(line => !line.StartsWith("refs/heads/release ", StringComparison.Ordinal))];
	}
}
