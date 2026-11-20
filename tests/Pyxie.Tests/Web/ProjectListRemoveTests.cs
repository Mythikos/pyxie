using Microsoft.AspNetCore.Components;

using Pyxie.Data.Entities;
using Pyxie.Runtime.Sandboxes;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>Removing a project from the projects panel. Every project that is not open has a Remove, also one whose folder is gone; the open one has none. Remove deletes the project's folder, so it only asks, in the row, with what it deletes, and hands the keyboard focus to Cancel; Cancel, or a removal that is refused, hands it back to Remove. Confirming removes the project and announces it, and a second press, from a double click or one the browser sent before it showed the removal, removes nothing more. Opening the project ends the question, and the pipeline running holds the confirmation back as it holds Open and Close.</summary>
public sealed class ProjectListRemoveTests
{
	private const string Warning = "Deletes its hub, cards, and settings.";
	private const string StopPipelineFirst = "Stop the pipeline first.";

	[Fact]
	public async Task Render_OffersRemoveOnEveryProjectThatIsNotOpenAndNotOnTheOpenOne()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true), ("moved", false));

		Assert.True((await list.Host.FindButtonAsync("Remove web")).IsClickable, "A closed project can be removed.");
		Assert.True((await list.Host.FindButtonAsync("Remove moved")).IsClickable, "A project whose folder is gone can be removed too.");
		Assert.DoesNotContain(await list.Host.ElementsAsync(), element => element.Name == "button" && element.Attribute("aria-label") == "Remove app");
		Assert.Empty(await list.Host.TextsWithClassAsync("project-confirm-text"));
	}

	[Fact]
	public async Task Remove_AsksInTheRowWhatItDeletesAndHandsTheFocusToCancelWithoutRemovingAnything()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		Project web = list.Projects[1];
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		Assert.Equal([Warning], await list.Host.TextsWithClassAsync("project-confirm-text"));
		GateRenderedElement cancel = await list.Host.FindButtonAsync("Cancel removing web");
		Assert.Equal(cancel.ReferenceId, FocusedId(list));
		GateRenderedElement confirm = await list.Host.FindButtonAsync("Confirm removing web");
		GateRenderedElement warning = Assert.Single(await list.Host.ElementsAsync(), element => ClassesOf(element).Contains("project-confirm-text"));
		Assert.Equal(warning.Attribute("id"), confirm.Attribute("aria-describedby"));
		Assert.False((await list.Host.FindButtonAsync("Remove web")).IsClickable, "Remove waits while it asks.");
		Assert.True(Directory.Exists(web.Folder), "Nothing is deleted before the person confirms.");
		Assert.Equal(2, (await list.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken)).Count);
		Assert.Empty(list.Rig.Sandboxes.ProjectRemovals);
		Assert.Equal(0, announcements);
	}

	[Fact]
	public async Task Cancel_KeepsTheProjectEndsTheQuestionAndHandsTheFocusBackToRemove()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		string removeId = await ReferenceIdAsync(list, "Remove web");
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Cancel removing web"));

		Assert.Empty(await list.Host.TextsWithClassAsync("project-confirm-text"));
		Assert.True((await list.Host.FindButtonAsync("Remove web")).IsClickable, "Remove can be pressed again.");
		Assert.Equal(removeId, FocusedId(list));
		Assert.True(Directory.Exists(list.Projects[1].Folder), "Cancelling deletes nothing.");
		Assert.Empty(list.Rig.Sandboxes.ProjectRemovals);
	}

	[Fact]
	public async Task Confirm_RemovesTheProjectAnnouncesItAndTheRowGoesWhenTheListIsReadAgain()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		Project web = list.Projects[1];
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Confirm removing web"));

		Assert.False(Directory.Exists(web.Folder), "The project's folder is deleted.");
		Assert.Equal(["app"], (await list.Rig.Catalog.ListAsync(TestContext.Current.CancellationToken)).Select(project => project.Name));
		Assert.Equal([SandboxNames.ProjectKey(web.Folder)], list.Rig.Sandboxes.ProjectRemovals);
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));

		await list.ReloadAsync();

		Assert.Equal(["app"], await list.Host.TextsWithClassAsync("project-name"));
		Assert.Empty(await list.Host.TextsWithClassAsync("project-confirm-text"));
	}

	[Fact]
	public async Task Confirm_WhenTheRemovalIsRefused_ShowsWhyOnTheRowAndHandsTheFocusBackToRemove()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		list.Rig.Sandboxes.RemoveProjectFailure = new SandboxUnavailableException("Docker is not running.");
		string removeId = await ReferenceIdAsync(list, "Remove web");
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Confirm removing web"));

		Assert.Equal(["Docker is not running, so the project's sandboxes cannot be removed. Start Docker Desktop, then remove the project again."], await list.Host.TextsWithClassAsync("project-error"));
		Assert.Empty(await list.Host.TextsWithClassAsync("project-confirm-text"));
		Assert.True((await list.Host.FindButtonAsync("Remove web")).IsClickable, "The person can try again.");
		Assert.Equal(removeId, FocusedId(list));
		Assert.True(Directory.Exists(list.Projects[1].Folder), "A refused removal deletes nothing.");
	}

	[Fact]
	public async Task Confirm_PressedAgainBeforeTheBrowserShowedTheRemoval_RemovesTheProjectOnce()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		int announcements = 0;
		list.Rig.Activity.ProjectChanged += () => Interlocked.Increment(ref announcements);
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));
		GateRenderedElement confirm = await list.Host.FindButtonAsync("Confirm removing web");
		await list.Host.HoldAcknowledgementsAsync();

		await list.Host.ClickAsync(confirm);
		await list.Host.ClickAsync(confirm, clicks: 2);
		await list.Host.AcknowledgeAsync();

		Assert.Single(list.Rig.Sandboxes.ProjectRemovals);
		Assert.Equal(1, announcements);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-error"));
	}

	[Fact]
	public async Task Open_WhileRemoveAsks_EndsTheQuestionAndRemovesNothing()
	{
		await using ProjectListHost list = await ProjectListHost.OpenAsync(("app", true), ("web", true));
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Open web"));
		await list.ReloadAsync();

		Assert.Equal("web", list.Rig.OpenProject.Current?.Name);
		Assert.Empty(await list.Host.TextsWithClassAsync("project-confirm-text"));
		Assert.DoesNotContain(await list.Host.ElementsAsync(), element => element.Name == "button" && element.Attribute("aria-label") == "Remove web");
		Assert.Empty(list.Rig.Sandboxes.ProjectRemovals);
	}

	[Fact]
	public async Task Confirm_WhileThePipelineRuns_WaitsAndSaysWhyAndCancelStillWorks()
	{
		await using ProjectListHost list = await ProjectListHost.OpenWithOpenProjectAsync("app", ("app", true), ("web", true));
		await list.Host.ClickAsync(await list.Host.FindButtonAsync("Remove web"));

		ResultAssert.Succeeded(list.Controller.Start());

		Assert.False((await list.Host.FindButtonAsync("Confirm removing web")).IsClickable, "Nothing is removed while the pipeline runs.");
		// The wrappers in row order: Close app, Open web, Remove web, then the question's Cancel and Confirm.
		IReadOnlyList<GateRenderedElement> elements = await list.Host.ElementsAsync();
		Assert.Equal([StopPipelineFirst, StopPipelineFirst, StopPipelineFirst, string.Empty, StopPipelineFirst], elements.Where(element => element.Name == "span" && ClassesOf(element).Contains("project-action")).Select(element => element.Attribute("title") ?? string.Empty));
		Assert.True((await list.Host.FindButtonAsync("Cancel removing web")).IsClickable, "The person can still back out.");

		list.Controller.Stop();

		Assert.True((await list.Host.FindButtonAsync("Confirm removing web")).IsClickable, "The removal can be confirmed once the pipeline stopped.");
	}

	private static string[] ClassesOf(GateRenderedElement element)
	{
		return (element.Attribute("class") ?? string.Empty).Split(' ');
	}

	// The host knows the reference a component captured for an element in the render that made the element, so a button that stays is read before the press that keeps it.
	private static async Task<string> ReferenceIdAsync(ProjectListHost list, string label)
	{
		return (await list.Host.FindButtonAsync(label)).ReferenceId ?? throw new InvalidOperationException($"The {label} button has no element reference.");
	}

	private static string? FocusedId(ProjectListHost list)
	{
		(string Identifier, object?[] Arguments) focus = list.Browser.Calls.Last(call => call.Identifier == GateStubJsRuntime.FocusIdentifier);

		return Assert.IsType<ElementReference>(focus.Arguments[0]).Id;
	}
}
