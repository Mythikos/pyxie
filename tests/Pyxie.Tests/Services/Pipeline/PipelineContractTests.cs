using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Data.Services.Rules;

using Xunit;

namespace Pyxie.Tests.Services.Pipeline;

/// <summary>The contract between Pyxie and the agents it runs: card branches, the words an agent reports with, and where the workspace files live.</summary>
public sealed class PipelineContractTests
{
	private static readonly string[] s_allWords = [PipelineContract.Completed, PipelineContract.Approved, PipelineContract.Rejected, PipelineContract.Resolved, PipelineContract.Errored, PipelineContract.Blocked];

	private static readonly RoundKind[] s_agentKinds = [RoundKind.Work, RoundKind.Review, RoundKind.Resolve];

	[Theory]
	[InlineData(1, "pyxie/card-1")]
	[InlineData(12, "pyxie/card-12")]
	[InlineData(1000, "pyxie/card-1000")]
	[InlineData(int.MaxValue, "pyxie/card-2147483647")]
	public void BranchName_IsThePrefixAndTheCardIdentifierWithoutSeparators(int cardId, string expected)
	{
		Assert.Equal(expected, PipelineContract.BranchName(cardId));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(7)]
	[InlineData(12)]
	[InlineData(999_999)]
	[InlineData(int.MaxValue)]
	public void TryParseBranchName_ReadsBackTheCardOfABranchNameItself(int cardId)
	{
		bool parsed = PipelineContract.TryParseBranchName(PipelineContract.BranchName(cardId), out int parsedId);

		Assert.True(parsed, "A card branch name must parse.");
		Assert.Equal(cardId, parsedId);
	}

	[Theory]
	[InlineData("")]
	[InlineData("main")]
	[InlineData("pyxie/card-")]
	[InlineData("pyxie/card-0")]
	[InlineData("pyxie/card-abc")]
	[InlineData("pyxie/card-1a")]
	[InlineData("pyxie/card-a1")]
	[InlineData("pyxie/card- 1")]
	[InlineData("pyxie/card-1 ")]
	[InlineData("pyxie/card--1")]
	[InlineData("pyxie/card-+1")]
	[InlineData("pyxie/card-1.5")]
	[InlineData("pyxie/card-1/extra")]
	[InlineData("pyxie/card-2147483648")]
	[InlineData("pyxie/card-99999999999999999999")]
	[InlineData("pyxie/card-١٢")]
	[InlineData("Pyxie/card-1")]
	[InlineData("pyxie/Card-1")]
	[InlineData("pyxie/card1")]
	[InlineData("pyxie/cards-1")]
	[InlineData("refs/heads/pyxie/card-1")]
	[InlineData("feature/pyxie/card-1")]
	[InlineData("pyxie/card-12-fix")]
	public void TryParseBranchName_RefusesAnythingThatIsNotExactlyACardBranchName(string branchName)
	{
		Assert.False(PipelineContract.TryParseBranchName(branchName, out int cardId), $"'{branchName}' is not a card branch.");
		Assert.Equal(0, cardId);
	}

	[Fact]
	public void TryParseBranchName_RefusesANumberWithLeadingZerosBecauseNoCardBranchIsNamedThatWay()
	{
		Assert.False(PipelineContract.TryParseBranchName("pyxie/card-012", out int cardId), "pyxie/card-012 is not the branch of card 12.");
		Assert.Equal(0, cardId);
	}

	[Theory]
	[InlineData(1, "Pyxie-Card: 1")]
	[InlineData(12, "Pyxie-Card: 12")]
	[InlineData(int.MaxValue, "Pyxie-Card: 2147483647")]
	public void CardTrailer_IsTheTrailerKeyAndTheCardIdentifierAsAGitTrailerLine(int cardId, string expected)
	{
		Assert.Equal("Pyxie-Card", PipelineContract.CardTrailerKey);
		Assert.Equal(expected, PipelineContract.CardTrailer(cardId));
	}

	[Fact]
	public void OutcomeWords_OfAWorkRound_AreCompletedErroredAndBlockedInThatOrder()
	{
		Assert.Equal(["completed", "errored", "blocked"], PipelineContract.OutcomeWords(RoundKind.Work));
	}

	[Fact]
	public void OutcomeWords_OfAReviewRound_AreApprovedRejectedErroredAndBlockedInThatOrder()
	{
		Assert.Equal(["approved", "rejected", "errored", "blocked"], PipelineContract.OutcomeWords(RoundKind.Review));
	}

	[Fact]
	public void OutcomeWords_OfAResolveRound_AreResolvedErroredAndBlockedInThatOrder()
	{
		Assert.Equal(["resolved", "errored", "blocked"], PipelineContract.OutcomeWords(RoundKind.Resolve));
	}

	[Fact]
	public void Resolved_IsTheWordForAMergeWhoseConflictsWereResolved()
	{
		Assert.Equal("resolved", PipelineContract.Resolved);
	}

	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Merge)]
	[InlineData((RoundKind)99)]
	public void OutcomeWords_OfARoundThatRunsNoAgent_AreNone(RoundKind kind)
	{
		Assert.Empty(PipelineContract.OutcomeWords(kind));
	}

	[Theory]
	[InlineData(RoundKind.Work, "completed", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Work, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Review, "approved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Review, "rejected", RoundResult.Failed)]
	[InlineData(RoundKind.Review, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Review, "blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Resolve, "resolved", RoundResult.Succeeded)]
	[InlineData(RoundKind.Resolve, "errored", RoundResult.Errored)]
	[InlineData(RoundKind.Resolve, "blocked", RoundResult.Blocked)]
	public void ResultOf_ReadsEachWordOfTheKindAsItsResult(RoundKind kind, string word, RoundResult expected)
	{
		Assert.Equal(expected, PipelineContract.ResultOf(kind, word));
	}

	[Theory]
	[InlineData(RoundKind.Work, "COMPLETED", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "Completed", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "  completed\r\n", RoundResult.Succeeded)]
	[InlineData(RoundKind.Work, "ErRoReD", RoundResult.Errored)]
	[InlineData(RoundKind.Review, "APPROVED", RoundResult.Succeeded)]
	[InlineData(RoundKind.Review, "\tRejected ", RoundResult.Failed)]
	[InlineData(RoundKind.Review, "Blocked", RoundResult.Blocked)]
	[InlineData(RoundKind.Resolve, "  RESOLVED\n", RoundResult.Succeeded)]
	public void ResultOf_IgnoresCaseAndSurroundingSpaces(RoundKind kind, string word, RoundResult expected)
	{
		Assert.Equal(expected, PipelineContract.ResultOf(kind, word));
	}

	[Theory]
	[InlineData(RoundKind.Work, "approved")]
	[InlineData(RoundKind.Work, "rejected")]
	[InlineData(RoundKind.Review, "completed")]
	[InlineData(RoundKind.Work, "resolved")]
	[InlineData(RoundKind.Review, "resolved")]
	[InlineData(RoundKind.Resolve, "completed")]
	[InlineData(RoundKind.Resolve, "approved")]
	[InlineData(RoundKind.Resolve, "rejected")]
	public void ResultOf_RefusesAWordThatBelongsToTheOtherKindOfRound(RoundKind kind, string word)
	{
		Assert.Null(PipelineContract.ResultOf(kind, word));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("done")]
	[InlineData("success")]
	[InlineData("complete")]
	[InlineData("completed!")]
	[InlineData("com pleted")]
	[InlineData("approved,rejected")]
	public void ResultOf_RefusesAMissingOrUnknownWord(string? word)
	{
		Assert.All(s_agentKinds, kind => Assert.Null(PipelineContract.ResultOf(kind, word)));
	}

	[Theory]
	[InlineData(RoundKind.Gate)]
	[InlineData(RoundKind.Merge)]
	[InlineData((RoundKind)99)]
	public void ResultOf_ReadsNothingForARoundThatRunsNoAgent(RoundKind kind)
	{
		Assert.All(s_allWords, word => Assert.Null(PipelineContract.ResultOf(kind, word)));
	}

	[Fact]
	public void ResultOf_AcceptsAWordExactlyWhenTheKindListsIt()
	{
		foreach (RoundKind kind in s_agentKinds)
		{
			foreach (string word in s_allWords)
			{
				bool listed = PipelineContract.OutcomeWords(kind).Contains(word);

				Assert.Equal(listed, PipelineContract.ResultOf(kind, word) is not null);
			}
		}
	}

	[Fact]
	public void ResultOf_NeverReadsAResultThatThePipelineCouldNotRecordForTheKind()
	{
		foreach (RoundKind kind in s_agentKinds)
		{
			foreach (string word in PipelineContract.OutcomeWords(kind))
			{
				RoundResult? result = PipelineContract.ResultOf(kind, word);

				Assert.NotNull(result);
				Assert.NotNull(PipelineRules.OutcomeOf(kind, result.Value));
			}
		}
	}

	[Fact]
	public void WorkspaceFiles_LiveInTheWorkspaceFolderThePromptNamesThem_AndBranchesStartWithThePrefix()
	{
		Assert.Equal(".pyxie/briefing.md", PipelineContract.BriefingFile);
		Assert.Equal(".pyxie/result.json", PipelineContract.ResultFile);
		Assert.StartsWith(PipelineContract.WorkspaceFolder + "/", PipelineContract.BriefingFile, StringComparison.Ordinal);
		Assert.StartsWith(PipelineContract.WorkspaceFolder + "/", PipelineContract.ResultFile, StringComparison.Ordinal);
		Assert.Contains(PipelineContract.BriefingFile, PipelineContract.Prompt, StringComparison.Ordinal);
		Assert.Contains(PipelineContract.ResultFile, PipelineContract.Prompt, StringComparison.Ordinal);
		Assert.StartsWith(PipelineContract.BranchPrefix, PipelineContract.BranchName(1), StringComparison.Ordinal);
	}
}
