using Pyxie.Data.Entities;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>A database that cannot be opened is a refusal on its row; the list stays usable and the project can be removed.</summary>
public sealed class ProjectListDatabaseFailureTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Open_WhenTheDatabaseCannotBeMigrated_ShowsTheReasonKeepsTheOpenProjectAndAllowsRemoval(bool fromAnotherBuild)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("web", ("app", true), ("web", true));
		Project app = list.Projects[0];
		await ProjectDatabaseFile.MakeUnopenableAsync(app.Folder, fromAnotherBuild, token);
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open app"));

		string error = Assert.Single(await list.Host.TextsWithClassAsync("project-error"));
		Assert.Contains("database", error, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("import", error, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("web", list.Rig.OpenProject.Current?.Name);
		Assert.Equal(0, announcements);
		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		GateRenderedElement alert = Assert.Single(elements, element => element.Attribute("role") == "alert");
		GateRenderedElement webName = Assert.Single(elements, element => element.Attribute("class") == "project-name" && element.Text == "web");
		Assert.True(elements.ToList().IndexOf(alert) < elements.ToList().IndexOf(webName), "The error belongs to app's row, before web's row.");
		Assert.True((await list.Host.FindButtonAsync("Open app")).IsClickable, "A refused open can be retried.");
		Assert.True((await list.Host.FindButtonAsync("Close web")).IsClickable, "The list remains usable after the refusal.");

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove app"));
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Confirm removing app"));
		await list.ReloadAsync();

		Assert.Equal(["web"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.False(Directory.Exists(app.Folder), "The refused project can be removed without reading its database.");
		Assert.Equal("web", list.Rig.OpenProject.Current?.Name);
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
	}
}
