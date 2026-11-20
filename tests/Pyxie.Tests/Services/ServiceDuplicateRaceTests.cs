using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class ServiceDuplicateRaceTests
{
	private const string ConcurrentCategory = "INSERT INTO Categories (Name, Color) VALUES ('Docs', '#EDEDE9')";

	private const string ConcurrentAgent = "INSERT INTO Agents (Name, Harness, Authentication, Command, Arguments, CanWork, CanReview, CanMerge) VALUES ('Alpha', 0, 1, 'claude', '', 1, 1, 0)";

	private static AgentInput Input(string name)
	{
		return new AgentInput(name, HarnessKind.ClaudeCode, "claude", string.Empty, true, true);
	}

	private static ConflictingInsertInterceptor Armed(string sql)
	{
		ConflictingInsertInterceptor interceptor = new(sql);
		interceptor.Arm();

		return interceptor;
	}

	[Fact]
	public async Task CategoryCreateAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		await using TestDatabase database = new(Armed(ConcurrentCategory));
		CategoryService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.CreateAsync("Docs", CategoryColors.Neutral, TestContext.Current.CancellationToken));

		Assert.Contains("already exists", error);
	}

	[Fact]
	public async Task CategoryRenameAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		await using TestDatabase database = new(Armed(ConcurrentCategory));
		CategoryService service = new(database.ContextFactory);

		string error = ResultAssert.Failed(await service.RenameAsync(1, "Docs", TestContext.Current.CancellationToken));

		Assert.Contains("already exists", error);
	}

	[Fact]
	public async Task AgentCreateAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		await using TestDatabase database = new(Armed(ConcurrentAgent));
		AgentService service = new(database.ContextFactory, database.Clock);

		string error = ResultAssert.Failed(await service.CreateAsync(Input("Alpha"), TestContext.Current.CancellationToken));

		Assert.Contains("already exists", error);
	}

	[Fact]
	public async Task AgentUpdateAsync_RefusesWhenConcurrentWriterTakesTheNameAfterTheCheck()
	{
		ConflictingInsertInterceptor interceptor = new(ConcurrentAgent);
		await using TestDatabase database = new(interceptor);
		Agent target = await TestData.AddAgentAsync(database, "Beta", canWork: true, canReview: true);
		AgentService service = new(database.ContextFactory, database.Clock);
		interceptor.Arm();

		string error = ResultAssert.Failed(await service.UpdateAsync(target.Id, Input("Alpha"), TestContext.Current.CancellationToken));

		Assert.Contains("already exists", error);
		Assert.Equal("Beta", (await TestData.FindAgentAsync(database, target.Id))?.Name);
	}
}
