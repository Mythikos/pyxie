using System.Globalization;
using System.Text;

using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;

namespace Pyxie.Runtime.Sandboxes;

/// <summary>Makes sandboxes as Linux containers with the <c>docker</c> command line (see <c>docs/sandboxing.md</c>). A container starts as root only to set its firewall, with <c>no-new-privileges</c>, and then idles; commands run in it as the unprivileged <c>node</c> user, which cannot change the firewall or gain privileges. It sees the host only through its input and output folders, its mounts, and its secrets, and its firewall refuses every address of the machine (see <see cref="HostAddresses"/>), so it never reaches Pyxie; the internet and the local network stay open for builds and agents.</summary>
public sealed class DockerSandboxes : ISandboxes
{
	private static readonly TimeSpan s_readyTimeout = TimeSpan.FromSeconds(90);
	private static readonly TimeSpan s_readyPoll = TimeSpan.FromMilliseconds(250);
	private static readonly TimeSpan s_versionTimeout = TimeSpan.FromSeconds(30);

	private readonly DockerCli _docker;
	private readonly DockerImages _images;
	private readonly TimeProvider _timeProvider;

	/// <summary>Initializes a new instance of the <see cref="DockerSandboxes"/> class.</summary>
	/// <param name="runner">The process runner the docker command line runs through.</param>
	/// <param name="timeProvider">The clock that paces the wait for a container to be ready.</param>
	public DockerSandboxes(ProcessRunner runner, TimeProvider timeProvider)
	{
		_docker = new DockerCli(runner);
		_images = new DockerImages(_docker);
		_timeProvider = timeProvider;
	}

	/// <inheritdoc/>
	public async Task<ServiceResult> CheckAvailableAsync(CancellationToken cancellationToken)
	{
		ProcessResult result;
		try
		{
			result = await _docker.RunAsync(["version", "--format", "{{.Server.Os}}"], cancellationToken, s_versionTimeout);
		}
		catch (SandboxUnavailableException exception)
		{
			return ServiceResult.Failure(exception.Message);
		}

		if (!result.Succeeded)
		{
			return ServiceResult.Failure(DockerCli.EngineDownMessage);
		}

		return result.StandardOutput.Trim() == "linux" ? ServiceResult.Success() : ServiceResult.Failure("Docker is running Windows containers, and Pyxie's sandboxes are Linux containers. Switch Docker Desktop to Linux containers.");
	}

	/// <inheritdoc/>
	public Task<ServiceResult<string>> EnsureImageAsync(SandboxImageRequest request, CancellationToken cancellationToken)
	{
		return _images.EnsureAsync(request, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ISandbox> StartAsync(SandboxSpec spec, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(spec.InputFolder);
		Directory.CreateDirectory(spec.OutputFolder);
		string specHash = SpecHash(spec);

		string? existing = await InspectAsync(spec.Name, cancellationToken);
		if (existing is not null)
		{
			if (spec.Keep && existing == $"true {specHash}")
			{
				// A round Pyxie stopped by dying leaves its agent running inside, so what it still runs is ended first. Host addresses change, so the firewall is set again.
				await _docker.RunAsync(["exec", "--user", "root", spec.Name, "pkill", "-KILL", "-u", "node"], cancellationToken);
				ProcessResult refreshed = await _docker.RunAsync(["exec", "--user", "root", spec.Name, "/usr/local/bin/pyxie-firewall", .. HostAddresses.Get()], cancellationToken);
				if (refreshed.Succeeded)
				{
					return new DockerSandbox(_docker, spec);
				}
			}

			await _docker.RunAsync(["rm", "--force", spec.Name], CancellationToken.None);
		}

		foreach (SandboxVolume volume in spec.Volumes)
		{
			await _docker.RunCheckedAsync(["volume", "create", "--label", $"{SandboxNames.ProjectLabel}={spec.ProjectKey}", "--label", $"{SandboxNames.CardLabel}={Card(spec.CardId)}", volume.Name], cancellationToken);
		}

		ProcessResult run = await _docker.RunAsync(RunArguments(spec, specHash), cancellationToken);
		if (!run.Succeeded)
		{
			throw new SandboxException($"The sandbox could not start: {DockerCli.LastLine(run.Output)}");
		}

		await WaitUntilReadyAsync(spec.Name, cancellationToken);

		return new DockerSandbox(_docker, spec);
	}

	/// <inheritdoc/>
	public async Task RemoveCardAsync(string projectKey, int cardId, CancellationToken cancellationToken)
	{
		string[] filters = ["--filter", $"label={SandboxNames.ProjectLabel}={projectKey}", "--filter", $"label={SandboxNames.CardLabel}={Card(cardId)}"];
		await RemoveContainersAsync(filters, cancellationToken);

		string volumes = await _docker.RunCheckedAsync(["volume", "ls", "--quiet", .. filters], cancellationToken);
		foreach (string volume in Lines(volumes))
		{
			await _docker.RunAsync(["volume", "rm", "--force", volume], cancellationToken);
		}
	}

	/// <inheritdoc/>
	public async Task RemoveLeftoverRoundsAsync(string projectKey, CancellationToken cancellationToken)
	{
		foreach (SandboxRole role in new[] { SandboxRole.Gates, SandboxRole.Review, SandboxRole.Resolve })
		{
			await RemoveContainersAsync(["--filter", $"label={SandboxNames.ProjectLabel}={projectKey}", "--filter", $"label={SandboxNames.RoleLabel}={SandboxNames.RoleName(role)}"], cancellationToken);
		}
	}

	/// <inheritdoc/>
	public async Task<IReadOnlySet<int>> ListCardsAsync(string projectKey, CancellationToken cancellationToken)
	{
		string filter = $"label={SandboxNames.ProjectLabel}={projectKey}";
		string format = $"{{{{.Label \"{SandboxNames.CardLabel}\"}}}}";
		string containers = await _docker.RunCheckedAsync(["ps", "--all", "--filter", filter, "--format", format], cancellationToken);
		string volumes = await _docker.RunCheckedAsync(["volume", "ls", "--filter", filter, "--format", format], cancellationToken);

		HashSet<int> cards = [];
		foreach (string line in Lines(containers).Concat(Lines(volumes)))
		{
			if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out int cardId))
			{
				cards.Add(cardId);
			}
		}

		return cards;
	}

	private static string Card(int cardId)
	{
		return cardId.ToString(CultureInfo.InvariantCulture);
	}

	private static IEnumerable<string> Lines(string output)
	{
		return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	// Everything a container is made with, so a kept one is picked up again only when the round would make the same one.
	private static string SpecHash(SandboxSpec spec)
	{
		StringBuilder text = new();
		text.AppendLine(spec.Image);
		foreach (SandboxVolume volume in spec.Volumes)
		{
			text.Append("volume ").Append(volume.Name).Append(' ').AppendLine(volume.PathInside);
		}

		foreach (SandboxMount mount in spec.Mounts)
		{
			text.Append("mount ").Append(mount.HostPath).Append(' ').Append(mount.PathInside).Append(' ').AppendLine(mount.ReadOnly ? "ro" : "rw");
		}

		foreach (SandboxSecret secret in spec.Secrets)
		{
			text.Append("secret ").Append(secret.HostPath).Append(' ').AppendLine(secret.EnvironmentVariable);
		}

		foreach ((string name, string value) in spec.Environment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			text.Append("env ").Append(name).Append('=').AppendLine(value);
		}

		text.Append("in ").AppendLine(spec.InputFolder);
		text.Append("out ").AppendLine(spec.OutputFolder);

		return SandboxNames.Hash(text.ToString())[..16];
	}

	private static List<string> RunArguments(SandboxSpec spec, string specHash)
	{
		List<string> arguments =
		[
			"run", "--detach", "--name", spec.Name,
			"--label", $"{SandboxNames.ProjectLabel}={spec.ProjectKey}",
			"--label", $"{SandboxNames.CardLabel}={Card(spec.CardId)}",
			"--label", $"{SandboxNames.RoleLabel}={SandboxNames.RoleName(spec.Role)}",
			"--label", $"{SandboxNames.SpecLabel}={specHash}",
			"--cap-add", "NET_ADMIN", "--cap-add", "NET_RAW", "--security-opt", "no-new-privileges",
			"--user", "root",
		];

		foreach ((string name, string value) in spec.Environment)
		{
			arguments.AddRange(["--env", $"{name}={value}"]);
		}

		foreach (SandboxVolume volume in spec.Volumes)
		{
			arguments.AddRange(["--mount", $"type=volume,source={volume.Name},target={volume.PathInside}"]);
		}

		arguments.AddRange(["--mount", Bind(spec.InputFolder, DockerSandbox.InputPathInside, readOnly: true)]);
		arguments.AddRange(["--mount", Bind(spec.OutputFolder, DockerSandbox.OutputPathInside, readOnly: false)]);

		foreach (SandboxSecret secret in spec.Secrets)
		{
			arguments.AddRange(["--mount", Bind(secret.HostPath, $"/pyxie/secrets/{secret.EnvironmentVariable}", readOnly: true)]);
		}

		foreach (SandboxMount mount in spec.Mounts)
		{
			arguments.AddRange(["--mount", Bind(mount.HostPath, mount.PathInside, mount.ReadOnly)]);
		}

		// The start script hands the machine's addresses to the firewall.
		arguments.Add(spec.Image);
		arguments.AddRange(HostAddresses.Get());

		return arguments;
	}

	// A --mount value is a comma-separated list, so a path with a comma or a quote cannot be named in it.
	private static string Bind(string hostPath, string pathInside, bool readOnly)
	{
		if (hostPath.Contains(',', StringComparison.Ordinal) || hostPath.Contains('"', StringComparison.Ordinal))
		{
			throw new SandboxException($"The folder {hostPath} has a comma or a quote in its path, which a sandbox cannot be given.");
		}

		return readOnly ? $"type=bind,source={hostPath},target={pathInside},readonly" : $"type=bind,source={hostPath},target={pathInside}";
	}

	// Returns "<running> <spec hash>" for a container that exists, or null.
	private async Task<string?> InspectAsync(string name, CancellationToken cancellationToken)
	{
		ProcessResult result = await _docker.RunAsync(["container", "inspect", "--format", $"{{{{.State.Running}}}} {{{{index .Config.Labels \"{SandboxNames.SpecLabel}\"}}}}", name], cancellationToken);

		return result.Succeeded ? result.StandardOutput.Trim() : null;
	}

	// The start script marks the container ready once its firewall is in place; a container that stopped instead says why in its log.
	private async Task WaitUntilReadyAsync(string name, CancellationToken cancellationToken)
	{
		long started = _timeProvider.GetTimestamp();

		while (true)
		{
			if ((await _docker.RunAsync(["exec", name, "test", "-f", "/run/pyxie-ready"], cancellationToken)).Succeeded)
			{
				return;
			}

			string? state = await InspectAsync(name, cancellationToken);
			if (state is null || state.StartsWith("false", StringComparison.Ordinal))
			{
				ProcessResult logs = await _docker.RunAsync(["logs", "--tail", "20", name], CancellationToken.None);
				await _docker.RunAsync(["rm", "--force", name], CancellationToken.None);

				throw new SandboxException($"The sandbox stopped as it started: {DockerCli.LastLine(logs.Output)}");
			}

			if (_timeProvider.GetElapsedTime(started) > s_readyTimeout)
			{
				await _docker.RunAsync(["rm", "--force", name], CancellationToken.None);

				throw new SandboxException("The sandbox did not become ready in time.");
			}

			await Task.Delay(s_readyPoll, _timeProvider, cancellationToken);
		}
	}

	private async Task RemoveContainersAsync(IReadOnlyList<string> filters, CancellationToken cancellationToken)
	{
		string containers = await _docker.RunCheckedAsync(["ps", "--all", "--quiet", .. filters], cancellationToken);
		foreach (string container in Lines(containers))
		{
			await _docker.RunAsync(["rm", "--force", container], cancellationToken);
		}
	}
}
