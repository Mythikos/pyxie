using Pyxie.Data.Entities;
using Pyxie.Data.Limits;
using Pyxie.Data.Services.Inputs;
using Pyxie.Web.Components.Board.Models;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class CardFormModelTests
{
	private const int CategoryId = 2;
	private const int WorkerId = 3;
	private const int ReviewerId = 4;

	[Fact]
	public void CreateNew_ChoosesNothingAndStartsWithTheDefaultAttempts()
	{
		CardFormModel form = CardFormModel.CreateNew();

		Assert.Equal(CardFormModel.NoSelection, form.CategoryId);
		Assert.Equal(CardFormModel.NoSelection, form.WorkerAgentId);
		Assert.Equal(CardFormModel.NoSelection, form.ReviewerAgentId);
		Assert.Equal(PyxieLimits.DefaultAttemptLimit, form.AttemptLimit);
		Assert.False(form.IsValid, "A new card needs a name, a description, a category, and a worker before it can be saved.");
	}

	[Fact]
	public void FromCard_HoldsTheValuesOfTheStoredCard()
	{
		WorkCard card = StoredCard(reviewerAgentId: ReviewerId, attemptLimit: 7);

		CardFormModel form = CardFormModel.FromCard(card);

		Assert.Equal("Fix the login", form.Name);
		Assert.Equal("Users cannot sign in.", form.Description);
		Assert.Equal(CategoryId, form.CategoryId);
		Assert.Equal(WorkerId, form.WorkerAgentId);
		Assert.Equal(ReviewerId, form.ReviewerAgentId);
		Assert.Equal(7, form.AttemptLimit);
		Assert.True(form.IsValid, "A stored card can be saved as it is.");
	}

	[Fact]
	public void FromCard_ShowsACardWithoutAReviewerAsNone()
	{
		CardFormModel form = CardFormModel.FromCard(StoredCard(reviewerAgentId: null));

		Assert.Equal(CardFormModel.NoSelection, form.ReviewerAgentId);
	}

	[Fact]
	public void ToInput_TrimsTextAndCarriesEveryChoice()
	{
		CardFormModel form = new()
		{
			Name = "  Fix the login  ",
			Description = "\tUsers cannot sign in.\n",
			CategoryId = CategoryId,
			WorkerAgentId = WorkerId,
			ReviewerAgentId = ReviewerId,
			AttemptLimit = 7,
		};

		CardInput input = form.ToInput();

		Assert.Equal(new CardInput("Fix the login", "Users cannot sign in.", CategoryId, WorkerId, ReviewerId, 7), input);
	}

	[Fact]
	public void ToInput_MapsNoneToUnassigned()
	{
		CardFormModel form = ValidForm();
		form.ReviewerAgentId = CardFormModel.NoSelection;

		Assert.Null(form.ToInput().ReviewerAgentId);
	}

	[Fact]
	public void WorkerError_RequiresAWorkerWithTheMessageTheServiceReports()
	{
		CardFormModel form = ValidForm();
		form.WorkerAgentId = CardFormModel.NoSelection;

		Assert.Equal(CardInputRules.ValidateWorker(null), form.WorkerError);
		Assert.Equal("Worker is required.", form.WorkerError);
		Assert.False(form.IsValid, "A card without a worker cannot be saved.");
	}

	[Fact]
	public void WorkerError_IsClearOnceAWorkerIsChosen()
	{
		Assert.Null(ValidForm().WorkerError);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(PyxieLimits.DefaultAttemptLimit)]
	[InlineData(PyxieLimits.MaxAttemptLimit)]
	public void AttemptLimitError_AcceptsOneToTheMaximum(int attemptLimit)
	{
		CardFormModel form = ValidForm();
		form.AttemptLimit = attemptLimit;

		Assert.Null(form.AttemptLimitError);
		Assert.True(form.IsValid, "An attempt limit in range can be saved.");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(PyxieLimits.MaxAttemptLimit + 1)]
	public void AttemptLimitError_RefusesALimitOutOfRangeWithTheMessageTheServiceReports(int attemptLimit)
	{
		CardFormModel form = ValidForm();
		form.AttemptLimit = attemptLimit;

		Assert.Equal(CardInputRules.ValidateAttemptLimit(attemptLimit), form.AttemptLimitError);
		Assert.NotNull(form.AttemptLimitError);
		Assert.False(form.IsValid, "A limit out of range cannot be saved.");
	}

	[Theory]
	[InlineData("", "Description")]
	[InlineData("Name", "   ")]
	public void IsValid_RequiresNameAndDescription(string name, string description)
	{
		CardFormModel form = ValidForm();
		form.Name = name;
		form.Description = description;

		Assert.False(form.IsValid, "A blank name or description cannot be saved.");
	}

	[Fact]
	public void IsValid_RequiresACategory()
	{
		CardFormModel form = ValidForm();
		form.CategoryId = CardFormModel.NoSelection;

		Assert.Equal("Category is required.", form.CategoryError);
		Assert.False(form.IsValid, "A card without a category cannot be saved.");
	}

	[Fact]
	public void DiffersFrom_IsFalseForTheFormOfTheSameCard()
	{
		WorkCard card = StoredCard(reviewerAgentId: ReviewerId);

		Assert.False(CardFormModel.FromCard(card).DiffersFrom(card), "A form nobody touched has nothing to save.");
	}

	[Fact]
	public void DiffersFrom_IgnoresWhitespaceAroundText()
	{
		WorkCard card = StoredCard();
		CardFormModel form = CardFormModel.FromCard(card);
		form.Name = $"  {card.Name}  ";
		form.Description = $"{card.Description}\n";

		Assert.False(form.DiffersFrom(card), "Whitespace is trimmed on save, so it is not an edit.");
	}

	[Theory]
	[InlineData(nameof(CardFormModel.Name))]
	[InlineData(nameof(CardFormModel.Description))]
	[InlineData(nameof(CardFormModel.CategoryId))]
	[InlineData(nameof(CardFormModel.WorkerAgentId))]
	[InlineData(nameof(CardFormModel.ReviewerAgentId))]
	[InlineData(nameof(CardFormModel.AttemptLimit))]
	public void DiffersFrom_IsTrueWhenAnyFieldChanged(string field)
	{
		WorkCard card = StoredCard(reviewerAgentId: ReviewerId);
		CardFormModel form = CardFormModel.FromCard(card);

		switch (field)
		{
			case nameof(CardFormModel.Name):
				form.Name = "Another name";
				break;
			case nameof(CardFormModel.Description):
				form.Description = "Another description";
				break;
			case nameof(CardFormModel.CategoryId):
				form.CategoryId = CategoryId + 1;
				break;
			case nameof(CardFormModel.WorkerAgentId):
				form.WorkerAgentId = WorkerId + 1;
				break;
			case nameof(CardFormModel.ReviewerAgentId):
				form.ReviewerAgentId = CardFormModel.NoSelection;
				break;
			default:
				form.AttemptLimit = card.AttemptLimit + 1;
				break;
		}

		Assert.True(form.DiffersFrom(card), $"Changing {field} should count as an edit.");
	}

	private static CardFormModel ValidForm()
	{
		return new CardFormModel { Name = "Name", Description = "Description", CategoryId = CategoryId, WorkerAgentId = WorkerId };
	}

	private static WorkCard StoredCard(int? reviewerAgentId = null, int attemptLimit = PyxieLimits.DefaultAttemptLimit)
	{
		return new WorkCard
		{
			Id = 9,
			Name = "Fix the login",
			Description = "Users cannot sign in.",
			CategoryId = CategoryId,
			WorkerAgentId = WorkerId,
			ReviewerAgentId = reviewerAgentId,
			AttemptLimit = attemptLimit,
		};
	}
}
