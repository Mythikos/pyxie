using System.Text.Json;

using Microsoft.Extensions.Options;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="SandboxSignIns"/> over a workspace root and a <c>CODEX_HOME</c> of the test's own: Claude Code gets the saved token as a secret file, Codex gets a fresh copy of the person's sign-in as its own <c>CODEX_HOME</c>, and DSH receives only its selected credential. Each test points <c>CODEX_HOME</c> at a temporary folder and puts it back, so no test ever reads or copies the person's real Codex sign-in; that is only safe because <see cref="ProcessEnvironmentCollection"/> never runs beside another test.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SandboxSignInsTests : IDisposable
{
	private const string CodexHomeVariable = "CODEX_HOME";
	private const string CodexAuthFile = "auth.json";
	private const string ClaudeTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";
	private const string ClaudeToken = "sk-ant-oat01-pyxie-test-token";

	private readonly EnvironmentVariableScope _codexHome = new("CODEX_HOME", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
	private readonly EnvironmentVariableScope _dshHome = new("DSH_HOME", Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

	public void Dispose()
	{
		_dshHome.Dispose();
		_codexHome.Dispose();
	}

	[Fact]
	public async Task ForAgent_ClaudeCodeWithoutAToken_IsMissingAndSaysHowToMakeOne()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.Account);

		Assert.NotNull(signIn.Problem);
		Assert.Contains("claude setup-token", signIn.Problem, StringComparison.Ordinal);
		Assert.Contains(layout.ClaudeTokenPath, signIn.Problem, StringComparison.Ordinal);
		Assert.Empty(signIn.Secrets);
		Assert.Empty(signIn.Mounts);
	}

	[Fact]
	public async Task ForAgent_ClaudeCodeWithAnEmptyToken_IsMissing()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(layout.ClaudeTokenPath, string.Empty);

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.Account);

		Assert.True(signIn.Problem is not null, "An empty token file is no token: Claude Code would fail to sign in in every round.");
		Assert.Empty(signIn.Secrets);
	}

	[Fact]
	public async Task ForAgent_ClaudeCodeWithAToken_GetsItAsASecretFileAndNeverAsText()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.Account);

		Assert.Null(signIn.Problem);
		Assert.Empty(signIn.Secrets);
		Assert.NotNull(signIn.PrepareAsync);
		Assert.Equal(ClaudeTokenVariable, signIn.Environment["PYXIE_AUTH"]);
		Assert.Empty(signIn.Mounts);
		Assert.All(signIn.Environment, pair => Assert.DoesNotContain(ClaudeToken, pair.Value, StringComparison.Ordinal));
		Assert.False(signIn.Environment.ContainsKey(ClaudeTokenVariable), "The token must reach the sandbox only through private round setup, never in the container configuration.");
	}

	[Fact]
	public async Task ForAgent_CodexNotSignedIn_IsMissingAndCopiesNothing()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		string signInFolder = scratch.Combine("sign-in");

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.Codex, AgentAuthentication.Account);

		Assert.NotNull(signIn.Problem);
		Assert.Contains("codex login", signIn.Problem, StringComparison.Ordinal);
		Assert.Contains(Path.Combine(home, CodexAuthFile), signIn.Problem, StringComparison.Ordinal);
		Assert.Empty(signIn.Mounts);
		Assert.False(Directory.Exists(signInFolder), "Nothing should be copied for a Codex that is not signed in.");
	}

	[Fact]
	public async Task Claude_ReportsBothCredentialsAndBlocksWhenTheSelectedOneIsMissing()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);
		WriteFile(layout.ClaudeApiKeyPath, "synthetic-key");
		SandboxSignIns signIns = new(layout);

		Assert.True(signIns.GetStatus().ClaudeAccount.Found);
		Assert.True(signIns.GetStatus().ClaudeApiKey.Found);
		Assert.Equal("ANTHROPIC_API_KEY", signIns.ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.ApiKey).Environment["PYXIE_AUTH"]);
		WriteFile(layout.ClaudeApiKeyPath, "invalid\nkey");
		Assert.NotNull(signIns.ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.ApiKey).Problem);
		Assert.Null(signIns.ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.Account).Problem);
		Assert.False(signIns.GetStatus().ClaudeApiKey.Found);
		File.Delete(layout.ClaudeTokenPath);
		WriteFile(layout.ClaudeApiKeyPath, "synthetic-key");
		Assert.NotNull(signIns.ForAgent(HarnessKind.ClaudeCode, AgentAuthentication.Account).Problem);
	}

	private const string Account = """{"auth_mode":"chatgpt","tokens":{"id_token":"e30.e30.synthetic","access_token":"test-access","refresh_token":"test-refresh","account_id":"saved-account","login_owner_id":"test-owner","oauth_provenance":{"issuer":"https://login.example.invalid","client_id":"test-client"}},"OPENAI_API_KEY":"unrelated"}""";

	[Fact]
	public async Task Codex_WithBothCredentials_UsesOnlyTheSelectedMethod()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(CodexLogin.PathOnHost, Account);
		WriteFile(layout.CodexApiKeyPath, "synthetic-key");
		SandboxSignIns signIns = new(layout);
		Assert.True(signIns.GetStatus().CodexApiKey.Found);
		Assert.True(signIns.GetStatus().CodexAccount.Found);
		string projected = Assert.IsType<string>(CodexLogin.ReadAccount(CodexLogin.PathOnHost));
		Assert.DoesNotContain("unrelated", projected, StringComparison.Ordinal);
		using JsonDocument document = JsonDocument.Parse(projected);
		JsonElement tokens = document.RootElement.GetProperty("tokens");
		Assert.Equal("saved-account", tokens.GetProperty("account_id").GetString());
		Assert.Equal("test-owner", tokens.GetProperty("login_owner_id").GetString());
		Assert.Equal("test-client", tokens.GetProperty("oauth_provenance").GetProperty("client_id").GetString());
		Assert.Equal("codex-account", signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.Account).Environment["PYXIE_AUTH"]);
		Assert.Equal("CODEX_API_KEY", signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.ApiKey).Environment["PYXIE_AUTH"]);
		WriteFile(layout.CodexApiKeyPath, "\r\n");
		Assert.NotNull(signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.ApiKey).Problem);
		Assert.False(signIns.GetStatus().CodexApiKey.Found);
		WriteFile(CodexLogin.PathOnHost, """{"auth_mode":"apikey","OPENAI_API_KEY":"saved-key"}""");
		Assert.NotNull(signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.Account).Problem);
		Assert.NotNull(signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.ApiKey).Problem);
		Assert.False(signIns.GetStatus().CodexAccount.Found);
		Assert.False(signIns.GetStatus().CodexApiKey.Found);
		WriteFile(layout.CodexApiKeyPath, "standalone-key");
		Assert.NotNull(signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.Account).Problem);
		Assert.Null(signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.ApiKey).Problem);
	}

	[Theory]
	[InlineData("not-json", false)]
	[InlineData("""{"auth_mode":"chatgpt","tokens":{"id_token":"one-word","access_token":"access","refresh_token":"refresh"}}""", false)]
	[InlineData("""{"auth_mode":"chatgpt","tokens":{"id_token":"abc.!!!.signature","access_token":"access","refresh_token":"refresh"}}""", false)]
	[InlineData("""{"auth_mode":"chatgpt","tokens":{"id_token":"e30.e30.synthetic","access_token":"access","refresh_token":"refresh"},"last_refresh":"invalid"}""", false)]
	[InlineData("""{"auth_mode":"chatgpt","tokens":{"id_token":"e30.e30.synthetic","access_token":"access"}}""", false)]
	[InlineData("""{"auth_mode":"apikey","tokens":{"id_token":"e30.e30.synthetic","access_token":"access","refresh_token":"refresh"}}""", false)]
	[InlineData("""{"OPENAI_API_KEY":"key","tokens":{"id_token":"e30.e30.synthetic","access_token":"access","refresh_token":"refresh"}}""", false)]
	[InlineData("""{"OPENAI_API_KEY":null,"tokens":{"id_token":"e30.e30.synthetic","access_token":"access","refresh_token":"refresh","account_id":"account"}}""", true)]
	public async Task CodexAccount_RecognizesOnlyTheStoredAccountMethod(string content, bool expected)
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(CodexLogin.PathOnHost, content);
		SandboxSignIns signIns = new(LayoutIn(scratch));

		Assert.Equal(expected, signIns.GetStatus().CodexAccount.Found);
		Assert.Equal(expected, signIns.ForAgent(HarnessKind.Codex, AgentAuthentication.Account).Problem is null);
		Assert.False(signIns.GetStatus().CodexApiKey.Found);
	}

	[Theory]
	[InlineData(HarnessKind.Other)]
	public async Task ForAgent_OtherHarnesses_GetNothingEvenWithSignInsSaved(HarnessKind harness)
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);
		WriteFile(Path.Combine(home, CodexAuthFile), Account);
		string signInFolder = scratch.Combine("sign-in");

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(harness, AgentAuthentication.None);

		Assert.Null(signIn.Problem);
		Assert.Empty(signIn.Secrets);
		Assert.Empty(signIn.Mounts);
		Assert.Empty(signIn.Environment);
		Assert.False(Directory.Exists(signInFolder), $"A {harness} agent must not be given a copy of anyone's sign-in.");
	}

	[Fact]
	public async Task GetStatus_NothingSaved_FindsNeitherAndSaysWhereItLooked()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);

		SandboxSignInStatus status = new SandboxSignIns(layout).GetStatus();

		Assert.Equal(new SandboxSignInStatus(new(layout.ClaudeTokenPath, false), new(layout.ClaudeApiKeyPath, false), new(Path.Combine(home, CodexAuthFile), false), new(layout.CodexApiKeyPath, false), new(DeepSeekAccountSignIn.StorePath, false), new(layout.DeepSeekApiKeyPath, false)), status);
	}

	[Fact]
	public async Task GetStatus_BothSaved_FindsBoth()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);
		WriteFile(Path.Combine(home, CodexAuthFile), Account);

		SandboxSignInStatus status = new SandboxSignIns(layout).GetStatus();

		Assert.True(status.ClaudeAccount.Found, "The saved Claude Code token was not found.");
		Assert.True(status.CodexAccount.Found, "The Codex sign-in in CODEX_HOME was not found.");
	}

	[Fact]
	public async Task GetStatus_EmptyToken_IsNotFound()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(layout.ClaudeTokenPath, string.Empty);

		Assert.False(new SandboxSignIns(layout).GetStatus().ClaudeAccount.Found, "An empty token file must not be reported as a token.");
	}

	// Only the path is read here: with CODEX_HOME unset, ForAgent would copy the person's real sign-in.
	[Theory]
	[InlineData(null)]
	[InlineData("   ")]
	public void NoCodexHome_LooksInTheProfilesCodexFolder(string? value)
	{
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, value);

		Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", CodexAuthFile), CodexLogin.PathOnHost);
	}

	[Fact]
	public async Task ForAgent_DeepSeekApiKey_RequiresOnlyTheSavedKeyAndExposesItAsAFile()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		SandboxSignIns signIns = new(layout);
		Assert.Contains(layout.DeepSeekApiKeyPath, signIns.ForAgent(HarnessKind.DeepSeek, AgentAuthentication.ApiKey).Problem, StringComparison.Ordinal);
		WriteFile(layout.DeepSeekApiKeyPath, string.Empty);
		Assert.NotNull(signIns.ForAgent(HarnessKind.DeepSeek, AgentAuthentication.ApiKey).Problem);
		WriteFile(layout.DeepSeekApiKeyPath, "test-key");
		SandboxSignIn signIn = signIns.ForAgent(HarnessKind.DeepSeek, AgentAuthentication.ApiKey);

		Assert.Null(signIn.Problem);
		Assert.Empty(signIn.Secrets);
		Assert.Equal("DEEPSEEK_API_KEY", signIn.Environment["PYXIE_AUTH"]);
		Assert.DoesNotContain("test-key", signIn.Environment.Values);
		Assert.NotNull(signIn.PrepareAsync);
		Assert.True(signIns.GetStatus().DeepSeekApiKey.Found, "Settings should find the saved API key.");
		Assert.NotNull(signIns.ForAgent(HarnessKind.DeepSeek, AgentAuthentication.Account).Problem);
		Assert.NotNull(signIns.ForAgent((HarnessKind)2, AgentAuthentication.None).Problem);
	}

	[Theory]
	[InlineData("version: 1\nrefs:\n  DEEPSEEK_API_KEY: other-secret")]
	[InlineData("version: 1\nrecords: [invalid")]
	[InlineData("version: 2\nrecords: {}")]
	[InlineData("version: 1\nrecords:\n  deepseek-account-platform/default: {kind: grant, payload: {version: 1, issuer: 'https://example.org', token: other-secret}}")]
	public async Task ForAgent_DeepSeekAccount_RequiresAnOfficialGrantAndKeepsParseErrorsPrivate(string content)
	{
		await using ScratchFolder scratch = new();
		using EnvironmentVariableScope dshHome = new("DSH_HOME", scratch.CreateSubfolder("dsh"));
		WriteFile(DeepSeekAccountSignIn.StorePath, content);
		SandboxSignIns signIns = new(LayoutIn(scratch));
		SandboxSignIn signIn = signIns.ForAgent(HarnessKind.DeepSeek, AgentAuthentication.Account);

		Assert.Contains(DeepSeekAccountSignIn.StorePath, signIn.Problem, StringComparison.Ordinal);
		Assert.DoesNotContain("other-secret", signIn.Problem, StringComparison.Ordinal);
		Assert.False(signIns.GetStatus().DeepSeekAccount.Found, "An unrelated key or malformed grant is not a DeepSeek account login.");
		Assert.Empty(signIn.Mounts);
		Assert.Null(signIn.PrepareAsync);
	}

	private static WorkspaceLayout LayoutIn(ScratchFolder scratch)
	{
		return new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = scratch.Combine("workspace") }));
	}

	private static void WriteFile(string path, string content)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException($"{path} has no folder.", nameof(path)));
		File.WriteAllText(path, content);
	}
}
