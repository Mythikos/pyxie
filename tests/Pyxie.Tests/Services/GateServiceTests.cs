using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Limits;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class GateServiceTests
{
	[Fact]
	public async Task ListAsync_IsEmptyWhenNoGatesExist()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		Assert.Empty(await service.ListAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task ListAsync_ReturnsGatesInTheOrderTheyRun_NotByNameOrCreation()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Zed", sortOrder: 0);
		await GateTestData.AddAtAsync(database, "Alpha", sortOrder: 2);
		await GateTestData.AddAtAsync(database, "Mid", sortOrder: 1);
		GateService service = new(database.ContextFactory);

		IReadOnlyList<Gate> gates = await service.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["Zed", "Mid", "Alpha"], gates.Select(gate => gate.Name));
	}

	[Fact]
	public async Task ListAsync_SettlesEqualPositionsByIdentifier()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "Second", sortOrder: 4);
		await GateTestData.AddAtAsync(database, "First", sortOrder: 1);
		await GateTestData.AddAtAsync(database, "Third", sortOrder: 4);
		GateService service = new(database.ContextFactory);

		IReadOnlyList<Gate> gates = await service.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(["First", "Second", "Third"], gates.Select(gate => gate.Name));
	}

	[Fact]
	public async Task CreateAsync_PersistsEveryField()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		Gate created = ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Lint", command: "lint.exe", arguments: "--strict -v", timeoutMinutes: 45, isEnabled: false), TestContext.Current.CancellationToken));

		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal(created.Id, stored.Id);
		Assert.Equal("Lint", stored.Name);
		Assert.Equal("lint.exe", stored.Command);
		Assert.Equal("--strict -v", stored.Arguments);
		Assert.Equal(45, stored.TimeoutMinutes);
		Assert.False(stored.IsEnabled, "IsEnabled should be stored.");
	}

	[Fact]
	public async Task CreateAsync_StoresAnEnabledGateAndTheLimitsOfTheTimeout()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Shortest", timeoutMinutes: 1), token));
		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Longest", timeoutMinutes: PyxieLimits.MaxGateTimeoutMinutes), token));

		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal([1, PyxieLimits.MaxGateTimeoutMinutes], stored.Select(gate => gate.TimeoutMinutes));
		Assert.All(stored, gate => Assert.True(gate.IsEnabled, "An enabled gate should be stored enabled."));
	}

	[Fact]
	public async Task CreateAsync_TrimsNameCommandAndArguments()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("  Tests \t", command: " dotnet ", arguments: "  test --no-build \r\n"), TestContext.Current.CancellationToken));

		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal("Tests", stored.Name);
		Assert.Equal("dotnet", stored.Command);
		Assert.Equal("test --no-build", stored.Arguments);
	}

	[Fact]
	public async Task CreateAsync_StoresBlankArgumentsAsEmpty()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create(arguments: "   "), TestContext.Current.CancellationToken));

		Assert.Equal(string.Empty, Assert.Single(await GateTestData.ReadAsync(database)).Arguments);
	}

	[Theory]
	[InlineData("", "dotnet", "test", 30, "Gate name is required.")]
	[InlineData("   ", "dotnet", "test", 30, "Gate name is required.")]
	[InlineData("Tests", "", "test", 30, "Command is required.")]
	[InlineData("Tests", "  \t", "test", 30, "Command is required.")]
	[InlineData("Tests", "dotnet", "test", 0, "Timeout must be between 1 and 240 minutes.")]
	[InlineData("Tests", "dotnet", "test", 241, "Timeout must be between 1 and 240 minutes.")]
	[InlineData("Tests", "dotnet", "test", -5, "Timeout must be between 1 and 240 minutes.")]
	public async Task CreateAsync_RejectsInvalidFieldsWithTheRuleMessage_AndStoresNothing(string name, string command, string arguments, int timeoutMinutes, string expected)
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(name, command, arguments, timeoutMinutes), TestContext.Current.CancellationToken));

		Assert.Equal(expected, error);
		Assert.Empty(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task CreateAsync_RejectsFieldsLongerThanTheirLimits_AndAcceptsFieldsAtTheLimits()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;
		string longName = new('n', PyxieLimits.MaxNameLength + 1);
		string longCommand = new('c', PyxieLimits.MaxCommandLength + 1);
		string longArguments = new('a', PyxieLimits.MaxArgumentsLength + 1);

		Assert.Equal("Gate name must be 100 characters or fewer.", ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(longName), token)));
		Assert.Equal("Command must be 500 characters or fewer.", ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(command: longCommand), token)));
		Assert.Equal("Arguments must be 2000 characters or fewer.", ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(arguments: longArguments), token)));
		Assert.Empty(await GateTestData.ReadAsync(database));

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create(new string('n', PyxieLimits.MaxNameLength), new string('c', PyxieLimits.MaxCommandLength), new string('a', PyxieLimits.MaxArgumentsLength)), token));
		Assert.Single(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task CreateAsync_ReportsTheFirstInvalidField_InTheOrderTheFormShowsThem()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		Assert.Equal("Gate name is required.", ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(name: "", command: "", timeoutMinutes: 0), token)));
		Assert.Equal("Command is required.", ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(command: "", timeoutMinutes: 0), token)));
	}

	[Theory]
	[InlineData("Tests", "A gate named 'Tests' already exists.")]
	[InlineData("tests", "A gate named 'tests' already exists.")]
	[InlineData("TESTS", "A gate named 'TESTS' already exists.")]
	[InlineData("  Tests  ", "A gate named 'Tests' already exists.")]
	public async Task CreateAsync_RejectsDuplicateNameIgnoringCaseAndSurroundingSpace(string name, string expected)
	{
		await using TestDatabase database = new();
		await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create(name), TestContext.Current.CancellationToken));

		Assert.Equal(expected, error);
		Assert.Single(await GateTestData.ReadAsync(database));
	}

	[Fact]
	public async Task CreateAsync_AllowsTwoGatesThatRunTheSameCommand()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Unit tests", command: "dotnet", arguments: "test"), token));
		ResultAssert.Succeeded(await service.CreateAsync(GateTestInput.Create("Integration tests", command: "dotnet", arguments: "test"), token));

		Assert.Equal(2, (await GateTestData.ReadAsync(database)).Count);
	}

	[Fact]
	public async Task UpdateAsync_ChangesEveryFieldAndKeepsIdentityAndPosition()
	{
		await using TestDatabase database = new();
		await GateTestData.AddAtAsync(database, "First", sortOrder: 0);
		Gate target = await GateTestData.AddAtAsync(database, "Build", sortOrder: 1);
		await GateTestData.AddAtAsync(database, "Last", sortOrder: 2);
		GateService service = new(database.ContextFactory);

		Gate updated = ResultAssert.Succeeded(await service.UpdateAsync(target.Id, GateTestInput.Create("Compile", command: "msbuild", arguments: "-t:Rebuild", timeoutMinutes: 90, isEnabled: false), TestContext.Current.CancellationToken));

		Assert.Equal(target.Id, updated.Id);
		List<Gate> stored = await GateTestData.ReadAsync(database);
		Assert.Equal(["First", "Compile", "Last"], stored.Select(gate => gate.Name));
		Assert.Equal([0, 1, 2], stored.Select(gate => gate.SortOrder));
		Gate changed = stored[1];
		Assert.Equal(target.Id, changed.Id);
		Assert.Equal("msbuild", changed.Command);
		Assert.Equal("-t:Rebuild", changed.Arguments);
		Assert.Equal(90, changed.TimeoutMinutes);
		Assert.False(changed.IsEnabled, "The gate should have been disabled.");
	}

	[Fact]
	public async Task UpdateAsync_ReenablesADisabledGate()
	{
		await using TestDatabase database = new();
		Gate disabled = await TestData.AddGateAsync(database, "Tests", isEnabled: false);
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.UpdateAsync(disabled.Id, GateTestInput.Create("Tests", isEnabled: true), TestContext.Current.CancellationToken));

		Assert.True(Assert.Single(await GateTestData.ReadAsync(database)).IsEnabled, "The gate should run again.");
	}

	[Fact]
	public async Task UpdateAsync_TrimsNameCommandAndArguments()
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.UpdateAsync(gate.Id, GateTestInput.Create("  Lint ", command: "\tlint ", arguments: " --fix  "), TestContext.Current.CancellationToken));

		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal("Lint", stored.Name);
		Assert.Equal("lint", stored.Command);
		Assert.Equal("--fix", stored.Arguments);
	}

	[Fact]
	public async Task UpdateAsync_ReportsAMissingGate()
	{
		await using TestDatabase database = new();
		await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.UpdateAsync(999, GateTestInput.Create("Other"), TestContext.Current.CancellationToken));

		Assert.Equal("Gate not found.", error);
		Assert.Equal("Tests", Assert.Single(await GateTestData.ReadAsync(database)).Name);
	}

	[Fact]
	public async Task UpdateAsync_AllowsKeepingOrRecasingItsOwnName()
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.UpdateAsync(gate.Id, GateTestInput.Create("Tests", arguments: "test --no-build"), token));
		ResultAssert.Succeeded(await service.UpdateAsync(gate.Id, GateTestInput.Create("TESTS"), token));

		Assert.Equal("TESTS", Assert.Single(await GateTestData.ReadAsync(database)).Name);
	}

	[Theory]
	[InlineData("Lint", "A gate named 'Lint' already exists.")]
	[InlineData("lint", "A gate named 'lint' already exists.")]
	[InlineData(" LINT ", "A gate named 'LINT' already exists.")]
	public async Task UpdateAsync_RejectsTheNameOfAnotherGateIgnoringCase_AndChangesNothing(string name, string expected)
	{
		await using TestDatabase database = new();
		Gate target = await TestData.AddGateAsync(database, "Tests", command: "dotnet", arguments: "test");
		await TestData.AddGateAsync(database, "Lint");
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.UpdateAsync(target.Id, GateTestInput.Create(name, command: "changed", timeoutMinutes: 5), TestContext.Current.CancellationToken));

		Assert.Equal(expected, error);
		Gate stored = (await GateTestData.ReadAsync(database)).Single(gate => gate.Id == target.Id);
		Assert.Equal("Tests", stored.Name);
		Assert.Equal("dotnet", stored.Command);
		Assert.Equal(PyxieLimits.DefaultGateTimeoutMinutes, stored.TimeoutMinutes);
	}

	[Theory]
	[InlineData("", "dotnet", "test", 30, "Gate name is required.")]
	[InlineData("Tests", " ", "test", 30, "Command is required.")]
	[InlineData("Tests", "dotnet", "test", 0, "Timeout must be between 1 and 240 minutes.")]
	[InlineData("Tests", "dotnet", "test", 241, "Timeout must be between 1 and 240 minutes.")]
	public async Task UpdateAsync_RejectsInvalidFieldsWithTheRuleMessage_AndChangesNothing(string name, string command, string arguments, int timeoutMinutes, string expected)
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.UpdateAsync(gate.Id, GateTestInput.Create(name, command, arguments, timeoutMinutes), TestContext.Current.CancellationToken));

		Assert.Equal(expected, error);
		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal("Tests", stored.Name);
		Assert.Equal("dotnet", stored.Command);
	}

	[Fact]
	public async Task UpdateAsync_ValidatesBeforeLookingForTheGate()
	{
		await using TestDatabase database = new();
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.UpdateAsync(999, GateTestInput.Create(name: ""), TestContext.Current.CancellationToken));

		Assert.Equal("Gate name is required.", error);
	}

	[Fact]
	public async Task UpdateAsync_AndDeleteAsync_NeverTouchCards()
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		WorkCard card = await TestData.AddCardAsync(database, "In the gates", CardLane.Gates);
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.UpdateAsync(gate.Id, GateTestInput.Create("Renamed", isEnabled: false), token));
		ResultAssert.Succeeded(await service.DeleteAsync(gate.Id, token));

		WorkCard? stored = await TestData.FindCardAsync(database, card.Id);
		Assert.NotNull(stored);
		Assert.Equal(CardLane.Gates, stored.Lane);
		Assert.Equal(card.Version, stored.Version);
		Assert.Equal(card.UpdatedUtc, stored.UpdatedUtc);
	}

	[Fact]
	public async Task DeleteAsync_RemovesTheGateAndKeepsTheOthers()
	{
		await using TestDatabase database = new();
		Dictionary<string, Gate> gates = await GateTestData.AddOrderedAsync(database, "Build", "Tests", "Lint");
		GateService service = new(database.ContextFactory);

		ResultAssert.Succeeded(await service.DeleteAsync(gates["Tests"].Id, TestContext.Current.CancellationToken));

		Assert.Equal("Build,Lint", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_ReportsAMissingGate_AndChangesNothing()
	{
		await using TestDatabase database = new();
		await GateTestData.AddOrderedAsync(database, "Build", "Tests");
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.DeleteAsync(999, TestContext.Current.CancellationToken));

		Assert.Equal("Gate not found.", error);
		Assert.Equal("Build,Tests", await GateTestData.ReadNamesAsync(database));
	}

	[Fact]
	public async Task DeleteAsync_ReportsAGateThatWasAlreadyDeleted()
	{
		await using TestDatabase database = new();
		Gate gate = await TestData.AddGateAsync(database, "Tests");
		GateService service = new(database.ContextFactory);
		CancellationToken token = TestContext.Current.CancellationToken;

		ResultAssert.Succeeded(await service.DeleteAsync(gate.Id, token));

		Assert.Equal("Gate not found.", ResultAssert.Failed(await service.DeleteAsync(gate.Id, token)));
	}
}
