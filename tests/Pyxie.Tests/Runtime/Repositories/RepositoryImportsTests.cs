using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Git;
using Pyxie.Tests.Runtime.Orchestration;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>Imports run in the background, one at a time, so a clone that takes minutes outlives the page that started it: the person comes back to find it running, failed with git's reason, or done. Only Pyxie shutting down cancels one. Each test makes its own <see cref="RepositoryImports"/> over the services of a <see cref="RuntimeHarness"/>, and a clone that is certainly still running is one of <see cref="UnresponsiveServer"/>.</summary>
public sealed class RepositoryImportsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static RepositoryImports Create(RuntimeHarness harness)
	{
		return new RepositoryImports(harness.Services.GetRequiredService<IServiceScopeFactory>());
	}

	// Cancels an import that is still running and waits until it has cleaned up, so nothing runs on after the harness is gone.
	private static async Task StopAsync(RepositoryImports imports)
	{
		imports.Dispose();
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);
	}

	private static async Task<Repository?> GetStoredAsync(RuntimeHarness harness)
	{
		if (harness.OpenProject.Current is null)
		{
			return null;
		}

		await using AsyncServiceScope scope = harness.CreateScope();

		return await scope.ServiceProvider.GetRequiredService<IRepositoryService>().GetAsync(Token);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_ForAFolder_ImportsItInTheBackgroundAndReportsRunningThenDone()
	{
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		using RepositoryImports imports = Create(harness);
		List<RepositoryImport?> seen = [];
		imports.Changed += () =>
		{
			lock (seen)
			{
				seen.Add(imports.Current);
			}
		};

		bool started = imports.TryStart(RepositorySource.Folder, source.Folder);
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.True(started);
		lock (seen)
		{
			Assert.Equal(2, seen.Count);
			Assert.Equal(new RepositoryImport(RepositorySource.Folder, source.Folder, null), seen[0]);
			Assert.True(seen[0]?.IsRunning, "The first announcement says the import is running.");
			Assert.Null(seen[1]);
		}

		Assert.Null(imports.Current);
		Repository? stored = await GetStoredAsync(harness);
		Assert.NotNull(stored);
		Assert.Equal((RepositorySource.Folder, "main"), (stored.Source, stored.TargetBranch));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_WhileAnImportIsRunning_RefusesAnotherAndKeepsReportingTheFirst()
	{
		await using UnresponsiveServer server = new();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		RepositoryImports imports = Create(harness);
		Assert.True(imports.TryStart(RepositorySource.Url, server.Url));
		await server.WaitForConnectionAsync(Token);
		int announced = 0;
		imports.Changed += () => announced++;

		bool second = imports.TryStart(RepositorySource.Folder, source.Folder);

		Assert.False(second, "One import runs at a time.");
		Assert.Equal(0, announced);
		Assert.Equal(new RepositoryImport(RepositorySource.Url, server.Url, null), imports.Current);
		Assert.False(imports.Running.IsCompleted, "The first import is still running.");
		Assert.Null(await GetStoredAsync(harness));
		await StopAsync(imports);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_ReturnsAtOnce_AndTheImportKeepsRunningUntilGitIsDone()
	{
		await using UnresponsiveServer server = new();
		await using RuntimeHarness harness = new();
		using RepositoryImports imports = Create(harness);

		Assert.True(imports.TryStart(RepositorySource.Url, server.Url));
		await server.WaitForConnectionAsync(Token);
		Task finished = await Task.WhenAny(imports.Running, Task.Delay(RuntimeTestTimeouts.IdleObservation, Token));

		Assert.False(ReferenceEquals(finished, imports.Running), "The import is waiting on the server, so it is still running although the caller has long returned.");
		Assert.True(imports.Current?.IsRunning, "A page that comes back finds the import running.");

		server.CloseConnections();
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		RepositoryImport? failed = imports.Current;
		Assert.NotNull(failed);
		Assert.False(failed.IsRunning, "The import ended.");
		Assert.StartsWith($"git could not clone {server.Url}: ", failed.Error, StringComparison.Ordinal);
		Assert.Null(await GetStoredAsync(harness));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_ForAFolderThatIsNotARepository_ReportsTheFailureThroughCurrentAndChanged()
	{
		await using ScratchFolder notARepository = ScratchFolder.Create();
		await using RuntimeHarness harness = new();
		using RepositoryImports imports = Create(harness);
		List<RepositoryImport?> seen = [];
		imports.Changed += () =>
		{
			lock (seen)
			{
				seen.Add(imports.Current);
			}
		};

		Assert.True(imports.TryStart(RepositorySource.Folder, notARepository.Folder));
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		RepositoryImport? failed = imports.Current;
		Assert.NotNull(failed);
		Assert.Equal(new RepositoryImport(RepositorySource.Folder, notARepository.Folder, $"The folder {notARepository.Folder} is not in a git repository."), failed);
		lock (seen)
		{
			Assert.Equal([true, false], seen.Select(import => import?.IsRunning));
		}

		Assert.Null(await GetStoredAsync(harness));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Dismiss_ForAFailedImport_ForgetsItAndAnnouncesTheChange()
	{
		await using ScratchFolder notARepository = ScratchFolder.Create();
		await using RuntimeHarness harness = new();
		using RepositoryImports imports = Create(harness);
		imports.TryStart(RepositorySource.Folder, notARepository.Folder);
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		int announced = 0;
		imports.Changed += () => announced++;

		imports.Dismiss();

		Assert.Null(imports.Current);
		Assert.Equal(1, announced);

		imports.Dismiss();

		Assert.Equal(1, announced);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Dismiss_WhileAnImportIsRunning_DoesNothing()
	{
		await using UnresponsiveServer server = new();
		await using RuntimeHarness harness = new();
		RepositoryImports imports = Create(harness);
		imports.TryStart(RepositorySource.Url, server.Url);
		await server.WaitForConnectionAsync(Token);
		int announced = 0;
		imports.Changed += () => announced++;

		imports.Dismiss();

		Assert.True(imports.Current?.IsRunning, "A running import is not dismissed.");
		Assert.Equal(0, announced);
		await StopAsync(imports);
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task TryStart_AfterAFailedImport_StartsANewOneAndReplacesTheFailure()
	{
		await using ScratchFolder notARepository = ScratchFolder.Create();
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new();
		using RepositoryImports imports = Create(harness);
		imports.TryStart(RepositorySource.Folder, notARepository.Folder);
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);
		Assert.False(imports.Current?.IsRunning, "The first import failed.");

		bool started = imports.TryStart(RepositorySource.Folder, source.Folder);
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.True(started);
		Assert.Null(imports.Current);
		Assert.NotNull(await GetStoredAsync(harness));
	}

	[Fact(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task Dispose_WhileACloneIsRunning_CancelsItAndRemovesWhatItCloned()
	{
		await using UnresponsiveServer server = new();
		await using RuntimeHarness harness = new();
		RepositoryImports imports = Create(harness);
		Assert.True(imports.TryStart(RepositorySource.Url, server.Url));
		await server.WaitForConnectionAsync(Token);
		string[] whileCloning = Directory.Exists(harness.Layout.ProjectsFolder) ? Directory.GetFileSystemEntries(harness.Layout.ProjectsFolder) : [];
		Assert.True(whileCloning.Length > 0, "git has started the clone, so the project's folder is already under the projects folder.");

		imports.Dispose();
		await imports.Running.WaitAsync(RuntimeTestTimeouts.Wait, Token);

		Assert.Equal("Pyxie shut down before the import finished.", imports.Current?.Error);
		string[] afterwards = Directory.Exists(harness.Layout.ProjectsFolder) ? Directory.GetFileSystemEntries(harness.Layout.ProjectsFolder) : [];
		Assert.True(afterwards.Length == 0, $"The cancelled clone must leave nothing behind, but the projects folder holds: {string.Join(", ", afterwards)}");
		Assert.Null(harness.OpenProject.Current);
		Assert.Null(await GetStoredAsync(harness));
	}
}
