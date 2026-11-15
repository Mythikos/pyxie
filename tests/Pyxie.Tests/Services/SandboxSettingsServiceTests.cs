using Pyxie.Data.Entities;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

/// <summary>A project's sandbox settings are the defaults until the first save inserts them; every later save updates that one row, stamped and with the next version.</summary>
public sealed class SandboxSettingsServiceTests
{
	private const string Dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get update\n";
	private const string Changed = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN apt-get install -y make\n";

	[Fact]
	public async Task GetAsync_WithNothingSaved_ReturnsTheDefaultDockerfileAtVersionZeroAndWritesNothing()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		SandboxSettings settings = await service.GetAsync(TestContext.Current.CancellationToken);

		Assert.Equal(SandboxDefaults.Dockerfile, settings.Dockerfile);
		Assert.Equal(0, settings.Version);
		Assert.Equal(0, settings.Id);
		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
	}

	[Fact]
	public async Task SaveAsync_FirstSave_InsertsTheSettingsAtVersionOneStampedWithTheClock()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		DateTime now = database.Clock.Tick();

		SandboxSettings saved = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Dockerfile), 0, TestContext.Current.CancellationToken));

		Assert.Equal(1, saved.Version);
		Assert.Equal(Dockerfile, saved.Dockerfile);
		Assert.Equal(now, saved.UpdatedUtc);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(saved.Id, stored.Id);
		Assert.Equal(1, stored.Version);
		Assert.Equal(Dockerfile, stored.Dockerfile);
		Assert.Equal(now, stored.UpdatedUtc);
		Assert.Equal(DateTimeKind.Utc, stored.UpdatedUtc.Kind);
	}

	[Fact]
	public async Task GetAsync_AfterASave_ReturnsTheSavedSettings()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings saved = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Dockerfile), 0, TestContext.Current.CancellationToken));

		SandboxSettings settings = await service.GetAsync(TestContext.Current.CancellationToken);

		Assert.Equal(saved.Id, settings.Id);
		Assert.Equal(Dockerfile, settings.Dockerfile);
		Assert.Equal(1, settings.Version);
		Assert.Equal(saved.UpdatedUtc, settings.UpdatedUtc);
		Assert.Equal(DateTimeKind.Utc, settings.UpdatedUtc.Kind);
	}

	[Fact]
	public async Task SaveAsync_WithTheVersionRead_UpdatesTheSameRowStampsItAndIncrementsTheVersion()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings first = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Dockerfile), 0, TestContext.Current.CancellationToken));
		DateTime later = database.Clock.Tick();

		SandboxSettings second = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Changed), first.Version, TestContext.Current.CancellationToken));

		Assert.Equal(first.Id, second.Id);
		Assert.Equal(2, second.Version);
		Assert.Equal(Changed, second.Dockerfile);
		Assert.Equal(later, second.UpdatedUtc);
		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(first.Id, stored.Id);
		Assert.Equal(2, stored.Version);
		Assert.Equal(Changed, stored.Dockerfile);
		Assert.Equal(later, stored.UpdatedUtc);
	}

	[Fact]
	public async Task SaveAsync_EachSaveWithTheVersionItWasGiven_KeepsLanding()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		int version = 0;

		for (int save = 1; save <= 3; save++)
		{
			SandboxSettings saved = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput($"FROM ${{PYXIE_BASE}}\nRUN echo {save}\n"), version, TestContext.Current.CancellationToken));
			version = saved.Version;
		}

		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(3, stored.Version);
		Assert.Equal("FROM ${PYXIE_BASE}\nRUN echo 3\n", stored.Dockerfile);
	}

	[Theory]
	[InlineData("ARG PYXIE_BASE\r\nFROM ${PYXIE_BASE}\r\nRUN make\r\n", "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN make\n")]
	[InlineData("FROM ${PYXIE_BASE}\rRUN make\r", "FROM ${PYXIE_BASE}\nRUN make\n")]
	[InlineData("\n\n  ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN make  \n\n\n", "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN make\n")]
	[InlineData("FROM ${PYXIE_BASE}\r\nRUN make \\\r\n\t&& make test\r\n\r\nRUN make install", "FROM ${PYXIE_BASE}\nRUN make \\\n\t&& make test\n\nRUN make install\n")]
	public async Task SaveAsync_StoresLfLineEndingsWithoutSurroundingWhitespaceAndOneFinalNewline(string entered, string expected)
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		SandboxSettings saved = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(entered), 0, TestContext.Current.CancellationToken));

		Assert.Equal(expected, saved.Dockerfile);
		Assert.Equal(expected, Assert.Single(await SandboxSettingsRows.ReadAsync(database)).Dockerfile);
	}

	[Fact]
	public async Task SaveAsync_WithCrOnlyLineEndingsBeforeTheFromLine_SavesItWithLfLineEndings()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		SandboxSettings saved = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput("ARG PYXIE_BASE\rFROM ${PYXIE_BASE}\rRUN make\r"), 0, TestContext.Current.CancellationToken));

		Assert.Equal("ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN make\n", saved.Dockerfile);
	}

	[Fact]
	public async Task SaveAsync_AnUpdate_NormalizesLineEndingsToo()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings first = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Dockerfile), 0, TestContext.Current.CancellationToken));

		SandboxSettings second = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Changed.Replace("\n", "\r\n", StringComparison.Ordinal) + "  \r\n"), first.Version, TestContext.Current.CancellationToken));

		Assert.Equal(Changed, second.Dockerfile);
		Assert.Equal(Changed, Assert.Single(await SandboxSettingsRows.ReadAsync(database)).Dockerfile);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("   \r\n")]
	[InlineData("FROM node:22-bookworm-slim\nRUN npm ci\n")]
	public async Task SaveAsync_OfAnInvalidDockerfile_ReturnsTheRulesMessageAndInsertsNothing(string? dockerfile)
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);

		string error = ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput(dockerfile), 0, TestContext.Current.CancellationToken));

		Assert.Equal(SandboxSettingsInputRules.ValidateDockerfile(dockerfile), error);
		Assert.Empty(await SandboxSettingsRows.ReadAsync(database));
	}

	[Fact]
	public async Task SaveAsync_OfAnInvalidDockerfile_LeavesTheSavedSettingsUntouched()
	{
		await using TestDatabase database = new();
		SandboxSettingsService service = new(database.ContextFactory, database.Clock);
		SandboxSettings first = ResultAssert.Succeeded(await service.SaveAsync(new SandboxSettingsInput(Dockerfile), 0, TestContext.Current.CancellationToken));
		database.Clock.Tick();

		ResultAssert.Failed(await service.SaveAsync(new SandboxSettingsInput("FROM node:22"), first.Version, TestContext.Current.CancellationToken));

		SandboxSettings stored = Assert.Single(await SandboxSettingsRows.ReadAsync(database));
		Assert.Equal(Dockerfile, stored.Dockerfile);
		Assert.Equal(1, stored.Version);
		Assert.Equal(first.UpdatedUtc, stored.UpdatedUtc);
	}
}
