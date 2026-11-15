using Microsoft.Extensions.Options;

using Pyxie.Data.Enums;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Runtime.Workspaces;
using Pyxie.Tests.Runtime.Processes;

using Xunit;

namespace Pyxie.Tests.Runtime.Sandboxes;

/// <summary><see cref="SandboxSignIns"/> over a workspace root and a <c>CODEX_HOME</c> of the test's own: Claude Code gets the saved token as a secret file, Codex gets a fresh copy of the person's sign-in as its own <c>CODEX_HOME</c>, and other harnesses get nothing. Each test points <c>CODEX_HOME</c> at a temporary folder and puts it back, so no test ever reads or copies the person's real Codex sign-in; that is only safe because <see cref="ProcessEnvironmentCollection"/> never runs beside another test.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class SandboxSignInsTests
{
	private const string CodexHomeVariable = "CODEX_HOME";
	private const string CodexAuthFile = "auth.json";
	private const string ClaudeTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";
	private const string ClaudeToken = "sk-ant-oat01-pyxie-test-token";

	[Fact]
	public async Task ForAgent_ClaudeCodeWithoutAToken_IsMissingAndSaysHowToMakeOne()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, scratch.Combine("sign-in"));

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

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, scratch.Combine("sign-in"));

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

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.ClaudeCode, scratch.Combine("sign-in"));

		Assert.Null(signIn.Problem);
		Assert.Equal(new SandboxSecret(layout.ClaudeTokenPath, ClaudeTokenVariable), Assert.Single(signIn.Secrets));
		Assert.Empty(signIn.Mounts);
		Assert.All(signIn.Environment, pair => Assert.DoesNotContain(ClaudeToken, pair.Value, StringComparison.Ordinal));
		Assert.False(signIn.Environment.ContainsKey(ClaudeTokenVariable), "The token must reach the sandbox only as a read-only file, never in the container's environment.");
	}

	[Fact]
	public async Task ForAgent_CodexNotSignedIn_IsMissingAndCopiesNothing()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		string signInFolder = scratch.Combine("sign-in");

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.Codex, signInFolder);

		Assert.NotNull(signIn.Problem);
		Assert.Contains("codex login", signIn.Problem, StringComparison.Ordinal);
		Assert.Contains(Path.Combine(home, CodexAuthFile), signIn.Problem, StringComparison.Ordinal);
		Assert.Empty(signIn.Mounts);
		Assert.False(Directory.Exists(signInFolder), "Nothing should be copied for a Codex that is not signed in.");
	}

	[Fact]
	public async Task ForAgent_CodexSignedIn_GetsACopyOfTheSignInAsItsCodexHome()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		WriteFile(Path.Combine(home, CodexAuthFile), "{\"tokens\":\"person\"}");
		string signInFolder = scratch.Combine("sign-in");

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(HarnessKind.Codex, signInFolder);

		Assert.Null(signIn.Problem);
		Assert.Empty(signIn.Secrets);
		SandboxMount mount = Assert.Single(signIn.Mounts);
		Assert.Equal(signInFolder, mount.HostPath);
		Assert.False(mount.ReadOnly, "Codex refreshes its tokens in its CODEX_HOME, so its copy must be writable.");
		Assert.Equal(mount.PathInside, signIn.Environment[CodexHomeVariable]);
		Assert.Equal("{\"tokens\":\"person\"}", File.ReadAllText(Path.Combine(signInFolder, CodexAuthFile)));
	}

	[Fact]
	public async Task ForAgent_SandboxChangesItsCodexCopy_LeavesThePersonsSignInAlone()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		string original = Path.Combine(home, CodexAuthFile);
		WriteFile(original, "{\"tokens\":\"person\"}");
		string signInFolder = scratch.Combine("sign-in");

		new SandboxSignIns(layout).ForAgent(HarnessKind.Codex, signInFolder);
		File.WriteAllText(Path.Combine(signInFolder, CodexAuthFile), "{\"tokens\":\"refreshed in the sandbox\"}");

		Assert.Equal("{\"tokens\":\"person\"}", File.ReadAllText(original));
	}

	[Fact]
	public async Task ForAgent_CodexAgain_CopiesTheSignInAsItIsNow()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		string original = Path.Combine(home, CodexAuthFile);
		string signInFolder = scratch.Combine("sign-in");
		SandboxSignIns signIns = new(layout);
		WriteFile(original, "{\"tokens\":\"first\"}");
		signIns.ForAgent(HarnessKind.Codex, signInFolder);

		WriteFile(original, "{\"tokens\":\"second\"}");
		signIns.ForAgent(HarnessKind.Codex, signInFolder);

		Assert.Equal("{\"tokens\":\"second\"}", File.ReadAllText(Path.Combine(signInFolder, CodexAuthFile)));
	}

	[Theory]
	[InlineData(HarnessKind.OpenCode)]
	[InlineData(HarnessKind.Other)]
	public async Task ForAgent_OtherHarnesses_GetNothingEvenWithSignInsSaved(HarnessKind harness)
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);
		WriteFile(Path.Combine(home, CodexAuthFile), "{}");
		string signInFolder = scratch.Combine("sign-in");

		SandboxSignIn signIn = new SandboxSignIns(layout).ForAgent(harness, signInFolder);

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

		Assert.Equal(new SandboxSignInStatus(layout.ClaudeTokenPath, ClaudeTokenFound: false, Path.Combine(home, CodexAuthFile), CodexSignInFound: false), status);
	}

	[Fact]
	public async Task GetStatus_BothSaved_FindsBoth()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		string home = scratch.CreateSubfolder("codex");
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, home);
		WriteFile(layout.ClaudeTokenPath, ClaudeToken);
		WriteFile(Path.Combine(home, CodexAuthFile), "{}");

		SandboxSignInStatus status = new SandboxSignIns(layout).GetStatus();

		Assert.True(status.ClaudeTokenFound, "The saved Claude Code token was not found.");
		Assert.True(status.CodexSignInFound, "The Codex sign-in in CODEX_HOME was not found.");
	}

	[Fact]
	public async Task GetStatus_EmptyToken_IsNotFound()
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, scratch.CreateSubfolder("codex"));
		WriteFile(layout.ClaudeTokenPath, string.Empty);

		Assert.False(new SandboxSignIns(layout).GetStatus().ClaudeTokenFound, "An empty token file must not be reported as a token.");
	}

	// Only the path is read here: with CODEX_HOME unset, ForAgent would copy the person's real sign-in.
	[Theory]
	[InlineData(null)]
	[InlineData("   ")]
	public async Task GetStatus_NoCodexHome_LooksInTheProfilesCodexFolder(string? value)
	{
		await using ScratchFolder scratch = new();
		WorkspaceLayout layout = LayoutIn(scratch);
		using EnvironmentVariableScope codexHome = new(CodexHomeVariable, value);

		SandboxSignInStatus status = new SandboxSignIns(layout).GetStatus();

		Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", CodexAuthFile), status.CodexSignInPath);
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
