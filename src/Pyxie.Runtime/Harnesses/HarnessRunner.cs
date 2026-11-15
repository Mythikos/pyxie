using System.Globalization;

using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Pipeline;
using Pyxie.Runtime.Orchestration;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Runtime.Harnesses;

/// <summary>Runs an agent's round in the card's sandbox: writes the briefing into the card's repository there, starts the agent's harness in it with the fixed prompt, and reads back the result file it writes. The agent's arguments are split and their tokens filled in with the round's values (<see cref="AgentArgumentTokens"/>). The prompt goes on standard input (Claude Code <c>-p</c> and <c>codex exec</c> read it there) except for OpenCode, which takes it as its last argument.</summary>
public sealed class HarnessRunner
{
	// The agent's own report carries what matters; this much of its console output is kept for when it breaks.
	private const int MaxCapturedOutput = 60_000;

	/// <summary>Runs the agent of a work or review round in a sandbox.</summary>
	/// <param name="work">The round, with the agent and briefing it recorded.</param>
	/// <param name="sandbox">The card's sandbox.</param>
	/// <param name="repository">The card's repository, as the sandbox sees it.</param>
	/// <param name="logPath">Where the agent's full console output is kept.</param>
	/// <param name="timeout">How long the agent may run.</param>
	/// <param name="cancellationToken">A token that stops the agent when cancelled.</param>
	/// <returns>The verdict: what the agent reported, or why the round broke.</returns>
	/// <exception cref="OperationCanceledException">The token was cancelled; the agent has been stopped.</exception>
	public async Task<RoundVerdict> RunAsync(PipelineWork work, ISandbox sandbox, string repository, string logPath, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (work.Agent is not AgentSnapshot agent || !work.Kind.RunsAgent())
		{
			return RoundVerdict.Blocked("No agent was recorded for this round.");
		}

		if (string.IsNullOrWhiteSpace(agent.Command))
		{
			return RoundVerdict.Blocked($"Agent '{agent.Name}' has no command to run.");
		}

		if (AgentArgumentTokens.TryExpand(CommandLine.Split(agent.Arguments), work, out IReadOnlyList<string> expanded) is string tokenProblem)
		{
			return RoundVerdict.Blocked($"Agent '{agent.Name}' cannot start. {tokenProblem}");
		}

		string resultPath = SandboxPaths.Combine(repository, PipelineContract.ResultFile);
		await sandbox.WriteFileAsync(SandboxPaths.Combine(repository, PipelineContract.BriefingFile), work.Briefing ?? string.Empty, cancellationToken);
		await sandbox.DeleteFileAsync(resultPath, cancellationToken);

		List<string> arguments = [.. expanded];
		string? standardInput = PipelineContract.Prompt;
		if (agent.Harness == HarnessKind.OpenCode)
		{
			arguments.Add(PipelineContract.Prompt);
			standardInput = null;
		}

		SandboxCommand command = new(agent.Command, arguments, repository)
		{
			StandardInput = standardInput,
			Timeout = timeout,
			LogPath = logPath,
			MaxOutputLength = MaxCapturedOutput,
		};

		ProcessResult result = await sandbox.RunAsync(command, cancellationToken);

		switch (result.Outcome)
		{
			case ProcessOutcome.NotStarted:
				return RoundVerdict.Blocked($"Agent '{agent.Name}' could not start: {result.StartError}");

			case ProcessOutcome.TimedOut:
				return RoundVerdict.Errored($"Agent '{agent.Name}' was stopped after running for {Duration.Describe(timeout)}.", result.Output);

			case ProcessOutcome.Exited when result.ExitCode != 0:
				return RoundVerdict.Errored(string.Create(CultureInfo.InvariantCulture, $"Agent '{agent.Name}' exited with code {result.ExitCode}."), result.Output);
		}

		string? text = await sandbox.ReadFileAsync(resultPath, HarnessResultFile.MaxLength + 1, cancellationToken);
		(RoundVerdict? verdict, string? problem) = HarnessResultFile.Read(text, work.Kind);

		return verdict ?? RoundVerdict.Errored(problem ?? $"The agent did not write {PipelineContract.ResultFile}.", result.Output);
	}
}
