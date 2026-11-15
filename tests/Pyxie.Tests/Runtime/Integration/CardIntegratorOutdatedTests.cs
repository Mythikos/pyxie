using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

public sealed class CardIntegratorOutdatedTests
{
	private const string MergedIntoTheCard = "main had moved on, so it was merged into pyxie/card-1; the gates check the combination.";

	[Fact]
	public async Task IntegrateAsync_WhenTheTargetMovedOnWithoutConflict_MergesTheTargetIntoTheCardBranchAndReportsOutdated()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		string mainTip = await rig.PushToTargetAsync("main-moved-on.txt", "main moved on");
		IReadOnlyList<string> otherRefsBefore = OtherThanTheCardBranch(await rig.ListRefsAsync());

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Outdated, verdict.Result);
		Assert.Equal(MergedIntoTheCard, verdict.Note);
		Assert.Null(verdict.Report);
		string combined = await rig.TipAsync("pyxie/card-1");
		Assert.Equal($"{cardTip} {mainTip}", await rig.ParentsAsync(combined));
		Assert.Equal("Merge main into pyxie/card-1", await rig.SubjectAsync(combined));
		Assert.Equal("Pyxie|pyxie@localhost|Pyxie|pyxie@localhost", await rig.Hub.RunGitAsync("log", "-1", "--format=%an|%ae|%cn|%ce", combined));
		Assert.Equal(string.Empty, await rig.Hub.RunGitAsync("log", "-1", "--format=%(trailers:key=Pyxie-Card,valueonly)", combined));
		Assert.Equal(await rig.MergeInWorkerAsync(cardTip, mainTip), await rig.TreeAsync(combined));
		Assert.Equal("the card's work", await rig.Hub.ReadFileAtAsync(combined, "feature.txt"));
		Assert.Equal("main moved on", await rig.Hub.ReadFileAtAsync(combined, "main-moved-on.txt"));
		Assert.Equal(mainTip, await rig.TipAsync("main"));
		Assert.Equal(otherRefsBefore, OtherThanTheCardBranch(await rig.ListRefsAsync()));
	}

	[Fact]
	public async Task IntegrateAsync_AfterTheCardWasBroughtUpToDate_MergesItIntoTheTargetWithOnlyTheCardsCommitsInTheReport()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		string cardTip = await rig.CommitOnCardAsync(1, "feature.txt", "the card's work");
		string movedOn = await rig.PushToTargetAsync("main-moved-on.txt", "main moved on");
		RoundVerdict outdated = await rig.IntegrateAsync(1);
		string combined = await rig.TipAsync("pyxie/card-1");

		RoundVerdict merged = await rig.IntegrateAsync(1, sequence: 7);

		Assert.Equal(RoundResult.Outdated, outdated.Result);
		Assert.Equal(RoundResult.Succeeded, merged.Result);
		Assert.Equal($"{movedOn} {combined}", await rig.ParentsAsync("main"));
		Assert.Equal("the card's work", await rig.Hub.ReadFileAtAsync("main", "feature.txt"));
		Assert.Equal("main moved on", await rig.Hub.ReadFileAtAsync("main", "main-moved-on.txt"));
		Assert.NotNull(merged.Report);
		string[] commits = [.. merged.Report.ReplaceLineEndings("\n").Split('\n').Skip(2)];
		Assert.Equal(2, commits.Length);
		Assert.StartsWith($"- {combined[..7]}", commits[0], StringComparison.Ordinal);
		Assert.EndsWith(" Merge main into pyxie/card-1", commits[0], StringComparison.Ordinal);
		Assert.StartsWith($"- {cardTip[..7]}", commits[1], StringComparison.Ordinal);
		Assert.EndsWith(" Add feature.txt", commits[1], StringComparison.Ordinal);
	}

	private static List<string> OtherThanTheCardBranch(IReadOnlyList<string> refs)
	{
		return [.. refs.Where(line => !line.StartsWith("refs/heads/pyxie/card-1 ", StringComparison.Ordinal))];
	}
}
