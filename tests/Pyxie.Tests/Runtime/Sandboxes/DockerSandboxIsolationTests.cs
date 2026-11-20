using System.Net.Sockets;

using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary>What keeps a sandbox in: a secret reaches its commands but never the container's configuration, its commands have no capabilities and cannot gain any, they cannot change the firewall, the firewall refuses a service on the host's loopback that a container without it reaches, and the internet stays open.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxIsolationTests
{
	private const string SecretVariable = "PYXIE_TEST_SECRET";

	private static readonly TimeSpan s_onlineCheckTimeout = TimeSpan.FromSeconds(10);

	// What an agent could try in order to open the firewall.
	private static readonly string[][] s_firewallChanges =
	[
		["iptables", "-F"],
		["iptables", "-P", "OUTPUT", "ACCEPT"],
		["iptables", "-D", "OUTPUT", "1"],
		["iptables", "-I", "OUTPUT", "1", "-j", "ACCEPT"],
		["ip6tables", "-F"],
	];

	private readonly DockerImageFixture _image;

	public DockerSandboxIsolationTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_Secret_ReachesCommandsButNeverTheContainersConfiguration()
	{
		await using DockerSandboxRig rig = new(_image);
		string secret = $"pyxie-secret-{Guid.NewGuid():N}";
		string secretFile = Path.Combine(rig.Folder, "secret.txt");
		string fileContents = secret + "\r\n";
		await File.WriteAllTextAsync(secretFile, fileContents, Token);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Review) with { Secrets = [new SandboxSecret(secretFile, SecretVariable)] };
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);

		ProcessResult seen = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", $"printf '%s.' \"${SecretVariable}\""], sandbox.WorkPath), Token);
		string inspect = await rig.Docker.RunCheckedAsync("inspect", sandbox.Name);
		ProcessResult containerEnvironment = await rig.Docker.RunAsync("exec", sandbox.Name, "env");
		ProcessResult overwrite = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", $"echo changed > /pyxie/secrets/{SecretVariable}"], sandbox.WorkPath), Token);

		Assert.Equal(secret + ".", seen.StandardOutput);
		Assert.False(inspect.Contains(secret, StringComparison.Ordinal), "The secret's text is in docker inspect, so anything that can list containers can read it.");
		Assert.True(containerEnvironment.Succeeded, containerEnvironment.Output);
		Assert.False(containerEnvironment.Output.Contains(secret, StringComparison.Ordinal), "The secret is in the container's own environment rather than handed to each command.");
		Assert.False(overwrite.Succeeded, "The sandbox could write to its secret file, which it must only read.");
		Assert.Equal(fileContents, await File.ReadAllTextAsync(secretFile, Token));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_AnyCommand_HasNoCapabilitiesAndCannotGainAny()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);

		ProcessResult status = await sandbox.RunAsync(new SandboxCommand("grep", ["-E", "^(CapEff|NoNewPrivs):", "/proc/self/status"], sandbox.WorkPath), Token);
		string securityOptions = await rig.Docker.RunCheckedAsync("inspect", "--format", "{{json .HostConfig.SecurityOpt}}", sandbox.Name);

		Assert.True(status.Succeeded, status.Output);
		Assert.Matches(@"CapEff:\s+0000000000000000", status.StandardOutput);
		Assert.Matches(@"NoNewPrivs:\s+1", status.StandardOutput);
		Assert.Contains("no-new-privileges", securityOptions, StringComparison.Ordinal);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_FirewallChanges_AreRefusedAndTheRulesStay()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);
		string before = await RulesAsync(rig, sandbox);
		Assert.Contains("REJECT", before, StringComparison.Ordinal);

		foreach (string[] change in s_firewallChanges)
		{
			ProcessResult result = await sandbox.RunAsync(new SandboxCommand(change[0], change[1..], sandbox.WorkPath), Token);

			Assert.False(result.Succeeded, $"The sandbox's user could run {string.Join(' ', change)}: {result.Output}");
		}

		Assert.Equal(before, await RulesAsync(rig, sandbox));
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_ServiceOnTheHostsLoopback_IsRefusedEvenAfterTryingToOpenTheFirewall()
	{
		await using DockerSandboxRig rig = new(_image);
		await using HostLoopbackListener listener = new();
		string url = $"http://host.docker.internal:{listener.Port}/";

		// The same image without the firewall shows the host's loopback is reachable here at all; without that, a refusal would prove nothing.
		ProcessResult control = await rig.Docker.RunAsync("run", "--rm", "--name", $"pyxie-{rig.ProjectKey}-control", "--label", $"{SandboxNames.ProjectLabel}={rig.ProjectKey}", "--entrypoint", "curl", rig.Image, "-sS", "-m", "15", url);
		if (!control.StandardOutput.Contains(HostLoopbackListener.Reply, StringComparison.Ordinal))
		{
			Assert.Skip($"A container without the firewall does not reach the host's loopback through host.docker.internal on this engine, so there is nothing to refuse: {control.Output}");
		}

		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker), Token);
		await AssertRefusedAsync(sandbox, url);

		foreach (string[] change in s_firewallChanges)
		{
			await sandbox.RunAsync(new SandboxCommand(change[0], change[1..], sandbox.WorkPath), Token);
		}

		await AssertRefusedAsync(sandbox, url);
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task RunAsync_Internet_IsReachableWhenTheHostIsOnline()
	{
		if (!await HostIsOnlineAsync())
		{
			Assert.Skip("This machine cannot reach example.com, so a sandbox cannot be expected to.");
		}

		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("curl", ["-sS", "-m", "30", "-o", "/dev/null", "-w", "%{http_code}", "https://example.com/"], sandbox.WorkPath), Token);

		Assert.True(result.Succeeded, $"The sandbox could not reach the internet, which builds and agents need: {result.Output}");
	}

	private static async Task<string> RulesAsync(DockerSandboxRig rig, ISandbox sandbox)
	{
		return await rig.Docker.RunCheckedAsync("exec", "--user", "root", sandbox.Name, "iptables", "-S");
	}

	private static async Task AssertRefusedAsync(ISandbox sandbox, string url)
	{
		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("curl", ["-sS", "-m", "15", url], sandbox.WorkPath), Token);

		Assert.False(result.Succeeded, $"The sandbox reached a service on the host's loopback: {result.Output}");
		Assert.DoesNotContain(HostLoopbackListener.Reply, result.Output, StringComparison.Ordinal);
	}

	private static async Task<bool> HostIsOnlineAsync()
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
		timeout.CancelAfter(s_onlineCheckTimeout);
		using TcpClient client = new();

		try
		{
			await client.ConnectAsync("example.com", 443, timeout.Token);

			return true;
		}
		catch (Exception exception) when (exception is SocketException or OperationCanceledException)
		{
			Token.ThrowIfCancellationRequested();

			return false;
		}
	}
}
