using Pyxie.Data.Entities;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web;

/// <summary>An editor's opening follows its <c>Visible</c> parameter: it opens on the item the dialog is shown with, keeps that item while the dialog is shown, and acts no more once it has finished or the dialog was hidden, however the dialog was hidden.</summary>
public sealed class EditorOpeningTests
{
	[Fact]
	public void Follow_WhenTheDialogIsShown_OpensItForTheItemItIsShownWith()
	{
		Gate build = NewGate(1, "Build");
		EditorOpening<Gate> opening = new();

		bool opened = opening.Follow(visible: true, build);

		Assert.True(opened, "Showing the dialog should open it.");
		Assert.True(opening.IsOpen, "The dialog should be open once it is shown.");
		Assert.Same(build, opening.Subject);
	}

	[Fact]
	public void Follow_WhileTheDialogIsShown_KeepsTheItemItWasOpenedFor()
	{
		Gate build = NewGate(1, "Build");
		EditorOpening<Gate> opening = new();
		opening.Follow(visible: true, build);

		bool opened = opening.Follow(visible: true, NewGate(2, "Test"));

		Assert.False(opened, "A dialog that is already shown should not open again.");
		Assert.True(opening.IsOpen, "The dialog should still be open.");
		Assert.Same(build, opening.Subject);
	}

	[Fact]
	public void Follow_WhenTheDialogIsHidden_EndsTheOpening()
	{
		Gate build = NewGate(1, "Build");
		EditorOpening<Gate> opening = new();
		opening.Follow(visible: true, build);

		bool opened = opening.Follow(visible: false, build);

		Assert.False(opened, "Hiding the dialog should not open it.");
		Assert.False(opening.IsOpen, "A dialog its parent hid should act no more.");
	}

	[Fact]
	public void Finish_EndsTheOpeningUntilTheDialogIsShownAgain()
	{
		Gate build = NewGate(1, "Build");
		EditorOpening<Gate> opening = new();
		opening.Follow(visible: true, build);

		opening.Finish();

		Assert.False(opening.IsOpen, "A finished dialog should act no more.");
		Assert.False(opening.Follow(visible: true, build), "A dialog that is still shown should not open again.");
		Assert.False(opening.IsOpen, "A finished dialog that is still shown should act no more.");
	}

	[Fact]
	public void Follow_WhenTheDialogIsShownAgain_OpensItForTheNewItem()
	{
		Gate build = NewGate(1, "Build");
		EditorOpening<Gate> opening = new();
		opening.Follow(visible: true, build);
		opening.Finish();
		opening.Follow(visible: false, build);

		bool opened = opening.Follow(visible: true, null);

		Assert.True(opened, "Showing the dialog again should open it again.");
		Assert.True(opening.IsOpen, "The dialog should be open again.");
		Assert.Null(opening.Subject);
	}

	private static Gate NewGate(int id, string name)
	{
		return new Gate { Id = id, Name = name, Command = "dotnet" };
	}
}
