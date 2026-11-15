using System.Text;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Integration;

/// <summary>A conflict is reported for whoever resolves it, the project's merge agent or the card's worker: the files that conflict and what landed on the target branch since the card's branch last took it in, with the names and tasks of the cards that landed it. The report does not say how to resolve; the briefing of each kind of round does.</summary>
public sealed class CardIntegratorConflictTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static string Normalized(RoundVerdict verdict)
	{
		return verdict.Report?.ReplaceLineEndings("\n") ?? string.Empty;
	}

	private static async Task<string> ShortAsync(IntegrationRig rig, string revision)
	{
		return await rig.Hub.RunGitAsync("rev-parse", "--short", revision);
	}

	// Writes commits straight onto the hub's target branch with one git process, so a test of many landed changes does not push each of them.
	private static async Task LandCommitsAsync(IntegrationRig rig, int count)
	{
		StringBuilder stream = new();
		for (int index = 1; index <= count; index++)
		{
			string message = $"Landed {index}";
			stream.Append("commit refs/heads/main\n");
			stream.Append($"committer Pyxie Tests <tests@example.com> {1_700_000_000 + index} +0000\n");
			stream.Append($"data {message.Length}\n{message}\n");
			stream.Append(index == 1 ? "from refs/heads/main^0\n" : string.Empty);
			stream.Append($"M 100644 inline landed-{index}.txt\ndata 1\nx\n\n");
		}

		using ProcessRunner runner = new(TimeProvider.System);
		ProcessResult result = await runner.RunAsync(new ProcessRequest("git", ["fast-import", "--quiet"], rig.Hub.Folder) { StandardInput = stream.ToString() }, Token);
		Assert.True(result.Succeeded, $"git fast-import failed: {result.Output}");
	}

	[Fact]
	public async Task IntegrateAsync_WhenTheTargetChangedTheSameFile_FailsWithTheFileAndWhatLandedWithoutMovingAnyBranch()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.CreateBranchAsync("release");
		await source.CheckoutAsync("release");
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "the card's version");
		string landed = await rig.PushToTargetAsync(TempGitRepository.InitialFile, "release's version");
		IReadOnlyList<string> refsBefore = await rig.ListRefsAsync();

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal("Merging release into pyxie/card-1 conflicts in 1 file.", verdict.Note);
		Assert.Equal($"Conflicting files:\n\n- {TempGitRepository.InitialFile}\n\nLanded on release since this branch last took it in, newest first:\n\n- {await ShortAsync(rig, landed)} Add {TempGitRepository.InitialFile}", Normalized(verdict));
		Assert.Equal(refsBefore, await rig.ListRefsAsync());
	}

	[Fact]
	public async Task IntegrateAsync_WhenSeveralFilesConflict_CountsThemAndListsEachOnceAndEveryChangeThatLanded()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await source.WriteFileAsync("one.txt", "base one");
		await source.WriteFileAsync("docs/two.txt", "base two");
		await source.WriteFileAsync("three.txt", "base three");
		await source.CommitAsync("Add the files both sides change");
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, "one.txt", "card one");
		await rig.CommitOnCardAsync(1, "docs/two.txt", "card two");
		await rig.CommitOnCardAsync(1, "three.txt", "card three");
		string first = await rig.PushToTargetAsync("one.txt", "main one");
		string second = await rig.PushToTargetAsync("docs/two.txt", "main two");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal("Merging main into pyxie/card-1 conflicts in 2 files.", verdict.Note);
		Assert.Equal($"Conflicting files:\n\n- docs/two.txt\n- one.txt\n\nLanded on main since this branch last took it in, newest first:\n\n- {await ShortAsync(rig, second)} Add docs/two.txt\n- {await ShortAsync(rig, first)} Add one.txt", Normalized(verdict));
	}

	[Fact]
	public async Task IntegrateAsync_WhenTheTargetDeletedAFileTheCardChanged_CountsItAsOneConflictingFile()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "the card's version");
		await rig.UpdatePersonAsync();
		await rig.Person.RunGitAsync("rm", "--quiet", TempGitRepository.InitialFile);
		await rig.Person.RunGitAsync("commit", "--quiet", "--message", $"Remove {TempGitRepository.InitialFile}");
		await rig.Person.PushAsync("main");

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal("Merging main into pyxie/card-1 conflicts in 1 file.", verdict.Note);
		Assert.Equal($"Conflicting files:\n\n- {TempGitRepository.InitialFile}\n\nLanded on main since this branch last took it in, newest first:\n\n- {await ShortAsync(rig, "main")} Remove {TempGitRepository.InitialFile}", Normalized(verdict));
	}

	[Fact]
	public async Task IntegrateAsync_WhenACardThatLandedIsKnown_QuotesItsTaskUnderItsMergeAndListsItsOwnCommitsNowhere()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "card one's version");
		await rig.CommitOnCardAsync(2, TempGitRepository.InitialFile, "card two's version");
		Assert.Equal(RoundResult.Succeeded, (await rig.IntegrateAsync(2)).Result);
		string merge = await rig.TipAsync("main");
		Dictionary<int, string> descriptions = new() { [2] = "Make the readme say\nsomething else.\n\nIn detail." };

		RoundVerdict verdict = await rig.IntegrateAsync(1, descriptions: descriptions);

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Equal($"Conflicting files:\n\n- {TempGitRepository.InitialFile}\n\nLanded on main since this branch last took it in, newest first:\n\n- {await ShortAsync(rig, merge)} Merge pyxie/card-2: Card 2\n  Its task:\n  > Make the readme say\n  > something else.\n  >\n  > In detail.", Normalized(verdict));
		Assert.DoesNotContain($"Add {TempGitRepository.InitialFile}", Normalized(verdict), StringComparison.Ordinal);
	}

	[Fact]
	public async Task IntegrateAsync_WhenAPersonPushedACommitWhoseSubjectLooksLikeAPyxieMerge_QuotesNoTaskBecauseOnlyTheTrailerNamesACard()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "card one's version");
		await rig.PushToTargetAsync(TempGitRepository.InitialFile, "main's version");
		await rig.Person.RunGitAsync("commit", "--quiet", "--allow-empty", "--message", "Merge pyxie/card-2: Card 2");
		await rig.Person.PushAsync("main");
		string lookAlike = await rig.TipAsync("main");
		Dictionary<int, string> descriptions = new() { [2] = "A task that must not be quoted." };

		RoundVerdict verdict = await rig.IntegrateAsync(1, descriptions: descriptions);

		Assert.Equal(RoundResult.Failed, verdict.Result);
		Assert.Contains($"- {await ShortAsync(rig, lookAlike)} Merge pyxie/card-2: Card 2", Normalized(verdict), StringComparison.Ordinal);
		Assert.DoesNotContain("Its task:", Normalized(verdict), StringComparison.Ordinal);
		Assert.DoesNotContain("A task that must not be quoted.", Normalized(verdict), StringComparison.Ordinal);
	}

	[Fact]
	public async Task IntegrateAsync_WhenACardThatLandedIsNotKnown_ListsItsMergeWithoutATask()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "card one's version");
		await rig.CommitOnCardAsync(2, TempGitRepository.InitialFile, "card two's version");
		await rig.IntegrateAsync(2);
		string merge = await rig.TipAsync("main");

		RoundVerdict verdict = await rig.IntegrateAsync(1, descriptions: new Dictionary<int, string> { [7] = "A card that did not land." });

		Assert.Equal($"Conflicting files:\n\n- {TempGitRepository.InitialFile}\n\nLanded on main since this branch last took it in, newest first:\n\n- {await ShortAsync(rig, merge)} Merge pyxie/card-2: Card 2", Normalized(verdict));
	}

	[Fact]
	public async Task IntegrateAsync_WhenALandedCardsTaskIsLong_QuotesItsFirstThousandCharactersAndAnEllipsis()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "card one's version");
		await rig.CommitOnCardAsync(2, TempGitRepository.InitialFile, "card two's version");
		await rig.IntegrateAsync(2);

		RoundVerdict verdict = await rig.IntegrateAsync(1, descriptions: new Dictionary<int, string> { [2] = new string('x', 1500) });

		Assert.EndsWith($"\n  Its task:\n  > {new string('x', 999)}…", Normalized(verdict), StringComparison.Ordinal);
	}

	[Fact]
	public async Task IntegrateAsync_WhenMoreThanThirtyChangesLanded_ListsTheNewestThirtyAndSaysThereAreOlderOnes()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "the card's version");
		await rig.PushToTargetAsync(TempGitRepository.InitialFile, "main's version");
		await LandCommitsAsync(rig, 31);

		RoundVerdict verdict = await rig.IntegrateAsync(1);

		string[] entries = [.. Normalized(verdict).Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal))];
		Assert.Equal(32, entries.Length);
		Assert.EndsWith(" Landed 31", entries[1], StringComparison.Ordinal);
		Assert.EndsWith(" Landed 2", entries[30], StringComparison.Ordinal);
		Assert.Equal("- (older changes are not listed)", entries[31]);
		Assert.DoesNotContain($"Add {TempGitRepository.InitialFile}", Normalized(verdict), StringComparison.Ordinal);
	}

	[Fact]
	public async Task IntegrateAsync_AfterAConflict_MergesOnceTheConflictIsResolvedInAMergeOfTheTarget()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using IntegrationRig rig = await IntegrationRig.CreateAsync(source);
		await rig.CommitOnCardAsync(1, TempGitRepository.InitialFile, "the card's version");
		string mainTip = await rig.PushToTargetAsync(TempGitRepository.InitialFile, "main's version");
		RoundVerdict conflicted = await rig.IntegrateAsync(1);

		// The next round: whoever resolves the conflict merges main into the card's branch, resolves it, and commits, and take-back brings the merge into the hub.
		await rig.PrepareWorkerAsync(1);
		ProcessResult merge = await rig.Hub.TryRunGitAsync("-C", rig.Worker.Folder, "merge", "--no-edit", "main");
		await File.WriteAllTextAsync(Path.Combine(rig.Worker.Folder, TempGitRepository.InitialFile), "resolved by the worker", Token);
		await rig.Worker.RunGitAsync("add", "--all");
		await rig.Worker.RunGitAsync("commit", "--quiet", "--no-edit");
		await rig.HandBackAsync(1);
		string resolvedTip = await rig.TipAsync("pyxie/card-1");
		RoundVerdict resolved = await rig.IntegrateAsync(1, sequence: 6);

		Assert.Equal(RoundResult.Failed, conflicted.Result);
		Assert.False(merge.Succeeded, "The merge of main must stop at the conflict the integrator reported.");
		Assert.Contains($"CONFLICT (content): Merge conflict in {TempGitRepository.InitialFile}", merge.Output, StringComparison.Ordinal);
		Assert.Equal(RoundResult.Succeeded, resolved.Result);
		Assert.Equal($"{mainTip} {resolvedTip}", await rig.ParentsAsync("main"));
		Assert.Equal("resolved by the worker", await rig.Hub.ReadFileAtAsync("main", TempGitRepository.InitialFile));
	}
}
