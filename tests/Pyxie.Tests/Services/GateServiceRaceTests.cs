using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Another writer can take a gate name between the service's check and its save; the unique index then rejects the save and the service must report a duplicate rather than fail.</summary>
public sealed class GateServiceRaceTests
{
	private const string ConcurrentGate = "INSERT INTO Gates (Name, Command, Arguments, TimeoutMinutes, SortOrder, IsEnabled) VALUES ('Tests', 'dotnet', 'test', 30, 0, 1)";

	private static ConflictingInsertInterceptor Armed(string sql)
	{
		ConflictingInsertInterceptor interceptor = new(sql);
		interceptor.Arm();

		return interceptor;
	}

	[Fact]
	public async Task CreateAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		await using TestDatabase database = new(Armed(ConcurrentGate));
		GateService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.CreateAsync(GateTestInput.Create("Tests", command: "other"), TestContext.Current.CancellationToken));

		Assert.Equal("A gate named 'Tests' already exists.", error);
		Gate stored = Assert.Single(await GateTestData.ReadAsync(database));
		Assert.Equal("dotnet", stored.Command);
	}

	[Fact]
	public async Task UpdateAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		ConflictingInsertInterceptor interceptor = new(ConcurrentGate);
		await using TestDatabase database = new(interceptor);
		Gate target = await GateTestData.AddAtAsync(database, "Beta", sortOrder: 0);
		GateService service = new(database.ContextFactory);
		interceptor.Arm();

		string error = ResultAssert.Failed(await service.UpdateAsync(target.Id, GateTestInput.Create("Tests", command: "other"), TestContext.Current.CancellationToken));

		Assert.Equal("A gate named 'Tests' already exists.", error);
		Gate stored = (await GateTestData.ReadAsync(database)).Single(gate => gate.Id == target.Id);
		Assert.Equal("Beta", stored.Name);
		Assert.Equal("dotnet", stored.Command);
	}
}
