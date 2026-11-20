using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Media;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Runtime.Orchestration;

public sealed class PipelineMediaRecoveryTests
{
	[Theory(Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Restart_ImportsOnlyTheInterruptedRoundsOwnPublishedOutput(bool matchingRound)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using TempGitRepository source = await TempGitRepository.CreateAsync();
		await using RuntimeHarness harness = new(roundTimeoutMinutes: 1);
		await harness.ConnectRepositoryAsync(source);
		Agent worker = await harness.AddWorkerAsync("Worker");
		WorkCard card = await harness.AddCardAsync("Left running", worker);
		await using AsyncServiceScope scope = harness.CreateScope();
		PipelineWork work = await scope.ServiceProvider.GetRequiredService<ICardPipelineService>().ClaimNextAsync(token) ?? throw new InvalidOperationException("No work.");
		string folder = WorkspaceLayout.SandboxFolder(harness.ProjectFolder, card.Id, "work");
		string media = Path.Combine(folder, "out", "media");
		Directory.CreateDirectory(Path.Combine(media, "files"));
		await File.WriteAllTextAsync(Path.Combine(folder, "media-round"), (matchingRound ? work.RoundId : work.RoundId + 1).ToString(CultureInfo.InvariantCulture), token);
		await File.WriteAllTextAsync(Path.Combine(media, "files", "capture.txt"), "before the crash", token);
		await File.WriteAllTextAsync(Path.Combine(media, "manifest.json"), "{\"version\":1,\"files\":[{\"path\":\"files/capture.txt\",\"name\":\"capture.txt\"}]}", token);
		await harness.StartOrchestratorAsync();
		await harness.WaitUntilAsync(async () => !(await harness.GetBoardAsync()).Any(item => item.History.IsRunning), "interrupted media recovery", RuntimeTestTimeouts.Wait);
		IReadOnlyList<MediaFile> files = ResultAssert.Succeeded(await scope.ServiceProvider.GetRequiredService<ICardMediaService>().ListAsync(harness.OpenProject.Current?.Id ?? 0, card.Id, token));
		if (matchingRound)
		{
			MediaFile file = Assert.Single(files);
			Assert.Equal(work.RoundId, file.RoundId);
			Assert.Equal("before the crash", await File.ReadAllTextAsync(MediaPaths.Object(harness.ProjectFolder, file.Id), token));
		}
		else
		{
			Assert.Empty(files);
		}
		await harness.StopOrchestratorAsync();
	}
}
