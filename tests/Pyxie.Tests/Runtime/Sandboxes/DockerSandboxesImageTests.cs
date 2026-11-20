using Microsoft.Extensions.Options;

using Pyxie.Data.Enums;
using Pyxie.Data.Sandboxes;
using Pyxie.Data.Services.Results;
using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Orchestration;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="DockerSandboxes.EnsureImageAsync"/> with a real engine: a project's image is built on Pyxie's base image from the project's Dockerfile, an image that is there is never built again, and a Dockerfile that fails says where its log is. The base image itself is built by <see cref="DockerImageFixture"/> the first time.</summary>
[Collection(DockerSandboxCollection.Name)]
public sealed class DockerSandboxesImageTests
{
	private readonly DockerImageFixture _image;

	public DockerSandboxesImageTests(DockerImageFixture image)
	{
		_image = image;
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_DefaultDockerfile_BuildsTheProjectImageOnTheBaseImage()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(rig.ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, log), Token);

		Assert.True(result.Succeeded, $"{result.Error}\n{ReadLog(log)}");
		Assert.NotNull(result.Value);
		Assert.Equal(DockerImages.ProjectTag(rig.ProjectKey, SandboxDefaults.Dockerfile), result.Value);
		Assert.True(ReadLog(log).Length > 0, "Building the project's image left no log.");
		await rig.Docker.RunCheckedAsync("image", "inspect", "--format", "{{.Id}}", DockerImages.BaseTag);
		string entrypoint = await rig.Docker.RunCheckedAsync("image", "inspect", "--format", "{{json .Config.Entrypoint}}", result.Value);
		Assert.True(entrypoint.Contains("pyxie-sandbox", StringComparison.Ordinal), $"The project's image is not built on Pyxie's base image: its entrypoint is {entrypoint}.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_ImageAlreadyThere_DoesNotBuildItAgain()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");
		SandboxImageRequest request = new(rig.ProjectKey, SandboxDefaults.Dockerfile, Rebuild: false, log);
		ServiceResult<string> first = await rig.Sandboxes.EnsureImageAsync(request, Token);
		Assert.True(first.Succeeded, $"{first.Error}\n{ReadLog(log)}");
		File.Delete(log);

		// A new instance knows nothing of what the first one built, so only the engine can tell it the image is there.
		using ProcessRunner runner = new(TimeProvider.System);
		ServiceResult<string> second = await new DockerSandboxes(runner, TimeProvider.System).EnsureImageAsync(request, Token);

		Assert.Equal(first, second);
		Assert.False(File.Exists(log), "The image was there, but it was built again.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task EnsureImageAsync_DockerfileThatFails_FailsAndPointsAtTheLog()
	{
		await using DockerSandboxRig rig = new(_image);
		string log = Path.Combine(rig.Folder, "image.log");
		string dockerfile = "ARG PYXIE_BASE\nFROM ${PYXIE_BASE}\nRUN echo pyxie-test-broken-step && exit 3\n";

		ServiceResult<string> result = await rig.Sandboxes.EnsureImageAsync(new SandboxImageRequest(rig.ProjectKey, dockerfile, Rebuild: false, log), Token);

		Assert.False(result.Succeeded, "A Dockerfile whose step fails cannot give an image.");
		Assert.Contains(log, result.Error, StringComparison.Ordinal);
		Assert.Contains("pyxie-test-broken-step", ReadLog(log), StringComparison.Ordinal);
		Assert.False((await rig.Docker.RunAsync("image", "inspect", DockerImages.ProjectTag(rig.ProjectKey, dockerfile))).Succeeded, "A failed build left an image behind.");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task StartAsync_DefaultImage_HasTheToolsAgentsAndGatesUse()
	{
		await using DockerSandboxRig rig = new(_image);
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Gates), Token);
		string script = "for tool in git node npm python3 curl claude codex dsh; do command -v \"$tool\" >/dev/null || { echo \"missing $tool\"; exit 1; }; done";

		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", script], sandbox.WorkPath), Token);

		Assert.True(result.Succeeded, $"The base image lacks a tool: {result.Output}");
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task DeepSeekAccount_InstallsOnlyItsGrantWithPrivatePermissionsAndReadsTheNextSignIn()
	{
		await using DockerSandboxRig rig = new(_image);
		string store = Path.Combine(rig.Folder, "host.yaml");
		const string Source = """
			version: 1
			refs: {OPENAI_API_KEY: unrelated-key}
			records:
			  deepseek-account-platform/default:
			    kind: grant
			    payload: {version: 1, issuer: 'https://platform.deepseek.com', token: account-one}
			  unrelated/provider: {kind: api-key, key: unrelated-key}
			""";
		await File.WriteAllTextAsync(store, Source, Token);
		SandboxSignIn signIn = DeepSeekAccountSignIn.ForAgent(store);
		Assert.Empty(signIn.Mounts);
		Assert.Empty(signIn.Secrets);
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker) with { Environment = signIn.Environment };
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		foreach (string token in new[] { "account-one", "account-two" })
		{
			string content = Source.Replace("account-one", token, StringComparison.Ordinal);
			await File.WriteAllTextAsync(store, content, Token);
			SandboxSignIn fresh = DeepSeekAccountSignIn.ForAgent(store);
			Assert.NotNull(fresh.PrepareAsync);
			await fresh.PrepareAsync(sandbox, Token);
			const string Check = """
				set -eu
				file="$DSH_HOME/.credentials.yaml"
				test "$(stat -c %a "$file")" = 600
				grep -q "$1" "$file"
				if grep -q unrelated "$file"; then exit 1; fi
				test -z "${DEEPSEEK_API_KEY:-}"
				grep -q 'provider: deepseek-account' "$DSH_HOME/profiles/headless/cordis.patch.yml"
				""";
			ProcessResult check = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", Check, "sh", token], sandbox.WorkPath), Token);
			Assert.True(check.Succeeded, "The DSH account profile must receive only the current account grant.");
			Assert.Equal(content, await File.ReadAllTextAsync(store, Token));
		}
	}

	[Theory(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	[InlineData(HarnessKind.Codex, AgentAuthentication.ApiKey, "CODEX_API_KEY", "OPENAI_API_KEY", "openai-api-key")]
	[InlineData(HarnessKind.ClaudeCode, AgentAuthentication.Account, "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "claude-oauth-token")]
	[InlineData(HarnessKind.ClaudeCode, AgentAuthentication.ApiKey, "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN", "anthropic-api-key")]
	[InlineData(HarnessKind.DeepSeek, AgentAuthentication.ApiKey, "DEEPSEEK_API_KEY", "DEEPSEEK_API_KEY", "deepseek-api-key")]
	public async Task KeyRound_UsesTheSelectedCredentialAndReadsItsReplacement(HarnessKind harness, AgentAuthentication authentication, string selected, string ambient, string file)
	{
		await using DockerSandboxRig rig = new(_image);
		WorkspaceLayout layout = new(Options.Create(new WorkspaceOptions { Root = rig.Folder }));
		string secrets = Path.Combine(rig.Folder, "secrets");
		Directory.CreateDirectory(secrets);
		string path = Path.Combine(secrets, file);
		await File.WriteAllTextAsync(path, "first-key\r\n", Token);
		SandboxSignIns signIns = new(layout);
		SandboxSignIn signIn = signIns.ForAgent(harness, authentication);
		Dictionary<string, string> environment = new(signIn.Environment) { [ambient] = "ambient-value" };
		SandboxSpec spec = rig.Spec(1, SandboxRole.Worker) with { Environment = environment };
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(spec, Token);
		foreach (string expected in new[] { "first-key", "rotated-key" })
		{
			await File.WriteAllTextAsync(path + ".new", expected + "\r\n", Token);
			File.Move(path + ".new", path, overwrite: true);
			SandboxSignIn fresh = signIns.ForAgent(harness, authentication);
			Assert.NotNull(fresh.PrepareAsync);
			await fresh.PrepareAsync(sandbox, Token);
			const string Check = """
				set -eu
				test "$(printenv "$1")" = "$3"
				test "$(stat -c %a "$PYXIE_KEY_FILE")" = 600
				if [ "$1" = CODEX_API_KEY ]; then test -d "$CODEX_HOME" && test ! -e "$CODEX_HOME/auth.json" || exit 1; fi
				if [ "$1" != "$2" ]; then ! printenv "$2" >/dev/null; fi
				""";
			ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", Check, "sh", selected, ambient, expected], sandbox.WorkPath), Token);
			Assert.True(result.Succeeded, "The runner should expose only the current selected credential.");
		}
	}

	[Fact(Skip = DockerTestSwitch.NeedsDocker, SkipUnless = nameof(DockerTestSwitch.IsOn), SkipType = typeof(DockerTestSwitch), Timeout = RuntimeTestTimeouts.TestMilliseconds)]
	public async Task CodexAccount_InstallsOnlyTheChatGptLoginAndLeavesTheHostCopyAlone()
	{
		await using DockerSandboxRig rig = new(_image);
		string path = Path.Combine(rig.Folder, "auth.json");
		const string Login = """{"auth_mode":"chatgpt","tokens":{"access_token":"access","refresh_token":"refresh","id_token":"e30.e30.synthetic"},"OPENAI_API_KEY":"unrelated"}""";
		await File.WriteAllTextAsync(path, Login, Token);
		SandboxSignIn signIn = SandboxSignIns.CodexAccount(path);
		Dictionary<string, string> environment = new(signIn.Environment) { ["OPENAI_API_KEY"] = "ambient", ["CODEX_API_KEY"] = "ambient" };
		await using ISandbox sandbox = await rig.Sandboxes.StartAsync(rig.Spec(1, SandboxRole.Worker) with { Environment = environment }, Token);
		Assert.NotNull(signIn.PrepareAsync);
		await signIn.PrepareAsync(sandbox, Token);
		const string Check = """
			set -eu
			file="$CODEX_HOME/auth.json"
			test "$(stat -c %a "$file")" = 600
			grep -q chatgpt "$file"
			if grep -q unrelated "$file" || printenv OPENAI_API_KEY >/dev/null || printenv CODEX_API_KEY >/dev/null; then exit 1; fi
			echo changed > "$file"
			""";
		ProcessResult result = await sandbox.RunAsync(new SandboxCommand("sh", ["-c", Check], sandbox.WorkPath), Token);
		Assert.True(result.Succeeded, "The account round should receive only a private copy of its ChatGPT login.");
		Assert.Equal(Login, await File.ReadAllTextAsync(path, Token));
	}

	private static string ReadLog(string path)
	{
		return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
	}
}
