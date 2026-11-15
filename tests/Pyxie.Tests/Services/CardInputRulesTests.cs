using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class CardInputRulesTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n")]
	public void ValidateName_RejectsMissingOrBlankName(string? name)
	{
		Assert.Equal("Name is required.", CardInputRules.ValidateName(name));
	}

	[Fact]
	public void ValidateName_AcceptsNameAtLimitAfterTrimming_AndRejectsOneOver()
	{
		Assert.Null(CardInputRules.ValidateName($"  {new string('n', PyxieLimits.MaxNameLength)}  "));
		Assert.NotNull(CardInputRules.ValidateName(new string('n', PyxieLimits.MaxNameLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\r\n\t")]
	public void ValidateDescription_RejectsMissingOrBlankDescription(string? description)
	{
		Assert.Equal("Description is required.", CardInputRules.ValidateDescription(description));
	}

	[Fact]
	public void ValidateDescription_AcceptsDescriptionAtLimit_AndRejectsOneOver()
	{
		Assert.Null(CardInputRules.ValidateDescription(new string('d', PyxieLimits.MaxDescriptionLength)));
		Assert.NotNull(CardInputRules.ValidateDescription(new string('d', PyxieLimits.MaxDescriptionLength + 1)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-1)]
	public void ValidateCategory_RejectsNoChoice(int? categoryId)
	{
		Assert.Equal("Category is required.", CardInputRules.ValidateCategory(categoryId));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(42)]
	public void ValidateCategory_AcceptsAnIdentifier(int categoryId)
	{
		Assert.Null(CardInputRules.ValidateCategory(categoryId));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-1)]
	public void ValidateWorker_RejectsNoChoice(int? workerAgentId)
	{
		Assert.Equal("Worker is required.", CardInputRules.ValidateWorker(workerAgentId));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(42)]
	public void ValidateWorker_AcceptsAnIdentifier(int workerAgentId)
	{
		Assert.Null(CardInputRules.ValidateWorker(workerAgentId));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(3)]
	[InlineData(PyxieLimits.MaxAttemptLimit)]
	public void ValidateAttemptLimit_AcceptsOneUpToTheMaximum(int attemptLimit)
	{
		Assert.Null(CardInputRules.ValidateAttemptLimit(attemptLimit));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(PyxieLimits.MaxAttemptLimit + 1)]
	[InlineData(int.MaxValue)]
	[InlineData(int.MinValue)]
	public void ValidateAttemptLimit_RejectsEverythingOutsideThatRangeAndNamesIt(int attemptLimit)
	{
		Assert.Equal("Attempts must be between 1 and 10.", CardInputRules.ValidateAttemptLimit(attemptLimit));
	}

	[Fact]
	public void CardInput_DefaultsTheAttemptLimitToTheDefaultLimit()
	{
		CardInput input = new("Card", "Text", CategoryId: 1, WorkerAgentId: 1, ReviewerAgentId: null);

		Assert.Equal(PyxieLimits.DefaultAttemptLimit, input.AttemptLimit);
		Assert.Equal(5, input.AttemptLimit);
	}
}
