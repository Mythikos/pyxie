using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>Two people can edit the sandbox settings in two tabs. A save lands only on the version its tab read, the first save included, so neither tab silently overwrites the other: the later save is refused with a message that says to reload, and the settings the other tab saved stay.</summary>
public sealed class SandboxSettingsServiceRaceTests
{
	private const string ChangedElsewhere = "The sandbox settings were changed elsewhere. Reload them, then save again.";
	private const string First = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo first\n";
	private const string Second = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo second\n";
	private const string Stale = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo stale\n";

	[Fact]
	public async Task SaveAsync_WithAStaleVersion_IsRefusedAndKeepsTheNewerSettings()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings read = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(First), 0, TestContext.Current.CancellationToken));
		DateTime secondSaved = database.Clock.Tick();
		ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Second), read.Version, TestContext.Current.CancellationToken));
		database.Clock.Tick();

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(Stale), read.Version, TestContext.Current.CancellationToken));

		Assert.Equal(ChangedElsewhere, error);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(Second, stored.Dockerfile);
		Assert.Equal(2, stored.Version);
		Assert.Equal(secondSaved, stored.UpdatedUtc);
	}

	[Fact]
	public async Task SaveAsync_ASecondFirstSave_IsRefusedAndKeepsTheFirst()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings firstTab = await service.GetAsync(TestContext.Current.CancellationToken);
		SandboxSettings secondTab = await service.GetAsync(TestContext.Current.CancellationToken);
		ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(First), firstTab.Version, TestContext.Current.CancellationToken));

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(Second), secondTab.Version, TestContext.Current.CancellationToken));

		Assert.Equal(ChangedElsewhere, error);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(First, stored.Dockerfile);
		Assert.Equal(1, stored.Version);
	}

	[Fact]
	public async Task SaveAsync_AFirstSaveThatAnotherFirstSaveBeatsToTheInsert_IsRefusedAndLeavesOnlyTheOther()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeTransaction();
		await using TestDatabase database = new(interceptor);
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		ServiceResult<SandboxSettings>? other = null;
		interceptor.Arm(async () => other = await service.SaveAsync(new SandboxSettingsInput(First), 0, TestContext.Current.CancellationToken));

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(Second), 0, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal(ChangedElsewhere, error);
		Assert.NotNull(other);
		ResultAssert.Succeeded(other);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(First, stored.Dockerfile);
		Assert.Equal(1, stored.Version);
	}

	[Fact]
	public async Task SaveAsync_ThatAnotherSaveOfTheSameVersionBeatsToTheUpdate_IsRefusedAndKeepsTheOther()
	{
		ConcurrentWriteInterceptor interceptor = ConcurrentWriteInterceptor.BeforeCommand("UPDATE");
		await using TestDatabase database = new(interceptor);
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings read = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(First), 0, TestContext.Current.CancellationToken));
		ServiceResult<SandboxSettings>? other = null;
		interceptor.Arm(async () => other = await service.SaveAsync(new SandboxSettingsInput(Second), read.Version, TestContext.Current.CancellationToken));

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(Stale), read.Version, TestContext.Current.CancellationToken));
		interceptor.AssertFired();

		Assert.Equal(ChangedElsewhere, error);
		Assert.NotNull(other);
		ResultAssert.Succeeded(other);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(Second, stored.Dockerfile);
		Assert.Equal(2, stored.Version);
	}

	[Fact]
	public async Task SaveAsync_WithAVersionWhileNothingIsSaved_IsRefusedAndInsertsNothing()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(First), 1, TestContext.Current.CancellationToken));

		Assert.Equal(ChangedElsewhere, error);
		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
	}
}
