using Pyxie.Data.Transfer.Planning;

using Xunit;

namespace Pyxie.Tests.Transfer.Planning;

/// <summary><see cref="NameMatcher"/> matches a file's items to a project's by name, ignoring case, so a matched item is updated in place and cards keep referring to it: an exact name wins, otherwise the first item that differs only in case, and no stored item is ever given to two file items. Matched and added items keep the file's order; removed items keep the project's.</summary>
public sealed class NameMatcherTests
{
	[Fact]
	public void Match_PrefersTheExactNameOverAnEarlierItemThatDiffersOnlyInCase()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "build"), (2, "Build")], ["Build"]);

		Assert.Equal([((2, "Build"), "Build")], plan.Matched);
		Assert.Equal([(1, "build")], plan.Removed);
		Assert.Empty(plan.Added);
	}

	[Fact]
	public void Match_WithoutTheExactName_TakesTheFirstItemThatDiffersOnlyInCase()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "Lint"), (2, "LINT")], ["lint"]);

		Assert.Equal([((1, "Lint"), "lint")], plan.Matched);
		Assert.Equal([(2, "LINT")], plan.Removed);
		Assert.Empty(plan.Added);
	}

	// The settings rules refuse a file with two names that differ only in case before anything is matched, but the matcher still never gives one stored item to both.
	[Fact]
	public void Match_GivesALaterFileItemTheFirstItemNoEarlierFileItemClaimed()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "Lint"), (2, "LINT")], ["lint", "lInt"]);

		Assert.Equal([((1, "Lint"), "lint"), ((2, "LINT"), "lInt")], plan.Matched);
		Assert.Empty(plan.Removed);
		Assert.Empty(plan.Added);
	}

	[Fact]
	public void Match_ClaimsEachStoredItemAtMostOnce_AndAddsAFileItemLeftWithoutOne()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "Build")], ["build", "Build"]);

		Assert.Equal([((1, "Build"), "build")], plan.Matched);
		Assert.Equal(["Build"], plan.Added);
		Assert.Empty(plan.Removed);
	}

	[Fact]
	public void Match_ListsMatchedAndAddedItemsInTheFileOrder_AndRemovedItemsInTheProjectOrder()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "Alpha"), (2, "Bravo"), (3, "Charlie"), (4, "Delta"), (5, "Echo")], ["Delta", "Foxtrot", "alpha", "Golf", "echo"]);

		Assert.Equal([((4, "Delta"), "Delta"), ((1, "Alpha"), "alpha"), ((5, "Echo"), "echo")], plan.Matched);
		Assert.Equal(["Foxtrot", "Golf"], plan.Added);
		Assert.Equal([(2, "Bravo"), (3, "Charlie")], plan.Removed);
	}

	[Fact]
	public void Match_OfAnEmptyProject_AddsEveryFileItem()
	{
		NamePlan<(int Id, string Name), string> plan = Match([], ["Build", "Tests"]);

		Assert.Empty(plan.Matched);
		Assert.Equal(["Build", "Tests"], plan.Added);
		Assert.Empty(plan.Removed);
	}

	[Fact]
	public void Match_OfAFileWithNoItems_RemovesEveryProjectItem()
	{
		NamePlan<(int Id, string Name), string> plan = Match([(1, "Build"), (2, "Tests")], []);

		Assert.Empty(plan.Matched);
		Assert.Empty(plan.Added);
		Assert.Equal([(1, "Build"), (2, "Tests")], plan.Removed);
	}

	[Theory]
	[InlineData("Build|Tests|Lint", "lint|BUILD|Docs")]
	[InlineData("build|Build|BUILD", "Build|bUILD")]
	[InlineData("A|a|B|b", "a|B|c")]
	[InlineData("", "One|Two")]
	[InlineData("One|Two", "")]
	public void Match_AccountsForEveryItemOnce(string stored, string written)
	{
		List<(int Id, string Name)> existing = [.. Split(stored).Select((name, index) => (index + 1, name))];
		List<string> incoming = Split(written);

		NamePlan<(int Id, string Name), string> plan = Match(existing, incoming);

		List<int> accounted = [.. plan.Matched.Select(pair => pair.Existing.Id), .. plan.Removed.Select(item => item.Id)];
		Assert.True(accounted.Order().SequenceEqual(existing.Select(item => item.Id)), $"Every stored item is either matched or removed, exactly once, but these were: {string.Join(", ", accounted)}.");
		Assert.True(plan.Matched.Count + plan.Added.Count == incoming.Count, "Every file item is either matched or added, exactly once.");
		Assert.All(plan.Matched, pair => Assert.True(string.Equals(pair.Existing.Name, pair.Incoming, StringComparison.OrdinalIgnoreCase), $"{pair.Incoming} was matched to {pair.Existing.Name}, which is another name."));
	}

	private static NamePlan<(int Id, string Name), string> Match(IReadOnlyList<(int Id, string Name)> existing, IReadOnlyList<string> incoming)
	{
		return NameMatcher.Match(existing, item => item.Name, incoming, name => name);
	}

	private static List<string> Split(string names)
	{
		return [.. names.Split('|', StringSplitOptions.RemoveEmptyEntries)];
	}
}
