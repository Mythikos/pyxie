using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Web.Components.Settings;
using Pyxie.Web.Components.Settings.Models;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Shows the import dialog as the Export and Import controls do: it owns whether the dialog is shown, shows it at once with the opening it was given, and hides it as soon as the dialog asks to be hidden. What the dialog reports is passed on to the test.</summary>
internal sealed class SettingsImportDialogHost : ComponentBase
{
	private bool _visible = true;

	/// <summary>Gets or sets what the dialog is shown for: a file's settings and their preview, or why the file could not be read.</summary>
	[Parameter]
	public required ServiceResult<SettingsImportOpening> Opening { get; set; }

	/// <summary>Gets or sets what is called with each value the dialog reports through <c>VisibleChanged</c>, before the host follows it.</summary>
	[Parameter]
	public Action<bool>? Reported { get; set; }

	/// <summary>Gets or sets what is called with the sections each import the dialog reports changed.</summary>
	[Parameter]
	public Action<IReadOnlySet<TransferSection>>? Imported { get; set; }

	/// <inheritdoc/>
	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenComponent<SettingsImportDialog>(0);
		builder.AddComponentParameter(1, nameof(SettingsImportDialog.Visible), _visible);
		builder.AddComponentParameter(2, nameof(SettingsImportDialog.VisibleChanged), EventCallback.Factory.Create<bool>(this, OnVisibleChanged));
		builder.AddComponentParameter(3, nameof(SettingsImportDialog.Opening), Opening);
		builder.AddComponentParameter(4, nameof(SettingsImportDialog.Imported), EventCallback.Factory.Create<IReadOnlySet<TransferSection>>(this, OnImported));
		builder.CloseComponent();
	}

	private void OnVisibleChanged(bool visible)
	{
		Reported?.Invoke(visible);
		_visible = visible;
	}

	private void OnImported(IReadOnlySet<TransferSection> changed)
	{
		Imported?.Invoke(changed);
	}
}
