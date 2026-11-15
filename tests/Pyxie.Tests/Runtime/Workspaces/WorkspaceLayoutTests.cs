using Microsoft.Extensions.Options;

using Pyxie.Runtime.Workspaces;

using Xunit;

namespace Pyxie.Tests.Runtime.Workspaces;

public sealed class WorkspaceLayoutTests
{
	[Fact]
	public void Root_WhenNothingIsConfigured_IsPyxieUnderTheLocalApplicationData()
	{
		WorkspaceLayout layout = Create(string.Empty);

		string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Pyxie");
		Assert.Equal(expected, layout.Root);
	}

	[Theory]
	[InlineData("   ")]
	[InlineData("\t")]
	public void Root_WhenTheConfiguredValueIsBlank_IsTheDefault(string configured)
	{
		Assert.Equal(Create(string.Empty).Root, Create(configured).Root);
	}

	[Fact]
	public void Root_WhenAFullPathIsConfigured_IsThatPathNormalized()
	{
		string baseFolder = Path.Combine(Path.GetTempPath(), "pyxie-layout-root");
		string configured = Path.Combine(baseFolder, "inner", "..", "workspaces");

		WorkspaceLayout layout = Create($"  {configured}  ");

		Assert.Equal(Path.Combine(baseFolder, "workspaces"), layout.Root);
	}

	[Theory]
	[InlineData("workspaces")]
	[InlineData("relative/path")]
	[InlineData("..\\up")]
	[InlineData(".\\here")]
	[InlineData("\\rooted-on-the-current-drive")]
	public void Constructor_WhenTheConfiguredRootIsNotAFullPath_RefusesAndNamesTheSetting(string configured)
	{
		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Create(configured));

		Assert.Contains("Workspace:Root", failure.Message, StringComparison.Ordinal);
		Assert.Contains(configured, failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void MachineFiles_AreTheDatabaseAndTheProjectsFolderInTheRoot()
	{
		string root = Path.Combine(Path.GetTempPath(), "pyxie-layout-root");
		WorkspaceLayout layout = Create(root);

		Assert.Equal(Path.Combine(root, "pyxie.db"), layout.MachineDatabasePath);
		Assert.Equal(Path.Combine(root, "projects"), layout.ProjectsFolder);
	}

	[Fact]
	public void ProjectFolders_HoldTheHubTheCardSandboxesTheRoundLogsAndTheImageLog()
	{
		string project = Path.Combine(Path.GetTempPath(), "pyxie-layout-root", "projects", "app");

		Assert.Equal(Path.Combine(project, "hub"), WorkspaceLayout.HubPath(project));
		Assert.Equal(Path.Combine(project, "sandboxes"), WorkspaceLayout.SandboxesFolder(project));
		Assert.Equal(Path.Combine(project, "sandboxes", "card-42"), WorkspaceLayout.CardSandboxesFolder(project, 42));
		Assert.Equal(Path.Combine(project, "sandboxes", "card-42", "work"), WorkspaceLayout.SandboxFolder(project, 42, "work"));
		Assert.Equal(Path.Combine(project, "runs", "card-42", "round-7.log"), WorkspaceLayout.RoundLogPath(project, 42, 7));
		Assert.Equal(Path.Combine(project, "runs", "image.log"), WorkspaceLayout.ImageLogPath(project));
	}

	[Fact]
	public void TheRoot_HoldsTheSignInsAgentsSandboxesGet()
	{
		string root = Path.Combine(Path.GetTempPath(), "pyxie-layout-root");
		WorkspaceLayout layout = Create(root);

		Assert.Equal(Path.Combine(root, "secrets"), layout.SecretsFolder);
		Assert.Equal(Path.Combine(root, "secrets", "claude-oauth-token"), layout.ClaudeTokenPath);
	}

	[Fact]
	public void Folders_OfDifferentProjectsAndCards_DoNotCollide()
	{
		string projects = Path.Combine(Path.GetTempPath(), "pyxie-layout-root", "projects");
		string app = Path.Combine(projects, "app");
		string other = Path.Combine(projects, "other");

		Assert.NotEqual(WorkspaceLayout.CardSandboxesFolder(app, 1), WorkspaceLayout.CardSandboxesFolder(app, 2));
		Assert.NotEqual(WorkspaceLayout.CardSandboxesFolder(app, 1), WorkspaceLayout.CardSandboxesFolder(other, 1));
		Assert.NotEqual(WorkspaceLayout.HubPath(app), WorkspaceLayout.HubPath(other));
		Assert.NotEqual(WorkspaceLayout.RoundLogPath(app, 1, 1), WorkspaceLayout.RoundLogPath(app, 1, 2));
	}

	private static WorkspaceLayout Create(string root)
	{
		return new WorkspaceLayout(Options.Create(new WorkspaceOptions { Root = root }));
	}
}
