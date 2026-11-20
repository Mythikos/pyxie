using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Settings;
using Pyxie.Web.Components.Settings.Models;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The import dialog rendered interactively and shown by a host that owns whether it is shown, as the Export and Import controls do, over the real settings transfer service on a test database or one a test gates. A test ticks its choices and presses its buttons as a person would, and reads what the dialog reported to its host.</summary>
internal sealed class SettingsImportDialogUnderTest : IAsyncDisposable
{
	/// <summary>What an import says when the project's settings changed after the file was read, so the changes the person reviewed are not the ones the import would make.</summary>
	public const string StaleMessage = "The project's settings changed after the file was read, so nothing was imported. Check what the import will do, then import again.";

	private readonly ServiceProvider _services;
	private readonly List<bool> _reported;
	private readonly List<IReadOnlySet<TransferSection>> _imported;

	private SettingsImportDialogUnderTest(ServiceProvider services, GateInteractiveHost host, List<bool> reported, List<IReadOnlySet<TransferSection>> imported)
	{
		_services = services;
		Host = host;
		_reported = reported;
		_imported = imported;
	}

	/// <summary>Gets the interactive host the dialog is rendered in.</summary>
	public GateInteractiveHost Host { get; }

	/// <summary>Gets what the dialog reported through <c>VisibleChanged</c>, in order: <see langword="false"/> asks its host to hide it.</summary>
	public IReadOnlyList<bool> Reported => [.. _reported];

	/// <summary>Gets the sections each import the dialog reported changed, in order.</summary>
	public IReadOnlyList<IReadOnlySet<TransferSection>> Imported => [.. _imported];

	/// <summary>Opens the dialog on a settings file, previewed against the stand-in project as Import does once the file is read.</summary>
	/// <param name="database">The database the project's settings are read from and imported into.</param>
	/// <param name="settings">The file's settings.</param>
	/// <param name="transfer">The settings transfer service the dialog imports through instead of the real one, or <see langword="null"/> for the real one.</param>
	/// <returns>The open dialog; the caller disposes it.</returns>
	public static async Task<SettingsImportDialogUnderTest> OpenAsync(TestDatabase database, ProjectSettings settings, ISettingsTransferService? transfer = null)
	{
		SettingsImportOpening opening = await SettingsTransferHost.OpenAsync(database, settings);

		return await OpenAsync(database, ServiceResult<SettingsImportOpening>.Success(opening), transfer);
	}

	/// <summary>Opens the dialog on what it is shown for: a file's settings and their preview, or why the file could not be read.</summary>
	/// <param name="database">The database the project's settings are read from and imported into.</param>
	/// <param name="opening">What the dialog is shown for.</param>
	/// <param name="transfer">The settings transfer service the dialog imports through instead of the real one, or <see langword="null"/> for the real one.</param>
	/// <returns>The open dialog; the caller disposes it.</returns>
	public static async Task<SettingsImportDialogUnderTest> OpenAsync(TestDatabase database, ServiceResult<SettingsImportOpening> opening, ISettingsTransferService? transfer = null)
	{
		List<bool> reported = [];
		List<IReadOnlySet<TransferSection>> imported = [];
		ServiceProvider services = SettingsTransferHost.CreateServices(database, transfer);
		SettingsImportDialogUnderTest dialog = new(services, new GateInteractiveHost(services), reported, imported);

		try
		{
			await dialog.Host.StartAsync<SettingsImportDialogHost>(new Dictionary<string, object?>
			{
				[nameof(SettingsImportDialogHost.Opening)] = opening,
				[nameof(SettingsImportDialogHost.Reported)] = (Action<bool>)reported.Add,
				[nameof(SettingsImportDialogHost.Imported)] = (Action<IReadOnlySet<TransferSection>>)imported.Add,
			});
		}
		catch
		{
			await dialog.DisposeAsync();
			throw;
		}

		return dialog;
	}

	/// <summary>Finds the import dialog's Import button among everything a host shows, which can hold other buttons named Import.</summary>
	/// <param name="host">The host the dialog is rendered in.</param>
	/// <returns>The button.</returns>
	public static async Task<GateRenderedElement> FindImportButtonAsync(GateInteractiveHost host)
	{
		IReadOnlyList<GateRenderedElement> elements = await host.ElementsAsync();
		int action = Assert.Single(Enumerable.Range(0, elements.Count), index => HasClass(elements[index], "import-action"));

		return elements.Skip(action + 1).First(element => element.Name == "button");
	}

	/// <summary>Reads the sections the import dialog offers among everything a host shows, as their choices are labelled.</summary>
	/// <param name="host">The host the dialog is rendered in.</param>
	/// <returns>The labels, in the order the dialog lists them.</returns>
	public static async Task<IReadOnlyList<string>> FindSectionsAsync(GateInteractiveHost host)
	{
		IReadOnlyList<GateRenderedElement> elements = await host.ElementsAsync();

		return [.. Enumerable.Range(0, elements.Count).Where(index => elements[index].Name == "li" && HasClass(elements[index], "import-section")).Select(index => elements.Skip(index + 1).First(element => HasClass(element, "ant-checkbox-wrapper")).Text.Trim())];
	}

	/// <summary>Finds the Import button.</summary>
	/// <returns>The button.</returns>
	public Task<GateRenderedElement> ImportButtonAsync()
	{
		return FindImportButtonAsync(Host);
	}

	/// <summary>Determines whether Import can be pressed.</summary>
	/// <returns><see langword="true"/> when it is enabled.</returns>
	public async Task<bool> CanImportAsync()
	{
		return (await ImportButtonAsync()).IsClickable;
	}

	/// <summary>Presses Import.</summary>
	/// <param name="clicks">The click count the browser reports: 1 for a click, 2 for the second click of a double click.</param>
	/// <returns>A task that completes when the press has been handled and the dialog rendered.</returns>
	public async Task PressImportAsync(long clicks = 1)
	{
		await Host.ClickAsync(await ImportButtonAsync(), clicks);
	}

	/// <summary>Reads the sections the dialog offers, as their choices are labelled.</summary>
	/// <returns>The labels, in the order the dialog lists them.</returns>
	public Task<IReadOnlyList<string>> SectionsAsync()
	{
		return FindSectionsAsync(Host);
	}

	/// <summary>Finds the choice of a section.</summary>
	/// <param name="section">The section.</param>
	/// <returns>The checkbox; its <c>checked</c> attribute is <c>true</c> while the section is chosen.</returns>
	public Task<GateRenderedElement> ChoiceAsync(TransferSection section)
	{
		return Host.FindCheckboxAsync(SettingsSections.Of(section).Title);
	}

	/// <summary>Determines whether a section is chosen.</summary>
	/// <param name="section">The section.</param>
	/// <returns><see langword="true"/> while its choice is ticked.</returns>
	public async Task<bool> IsPickedAsync(TransferSection section)
	{
		return (await ChoiceAsync(section)).Attribute("checked") == "true";
	}

	/// <summary>Ticks or clears the choice of a section, as clicking it does.</summary>
	/// <param name="section">The section.</param>
	/// <param name="picked">Whether the section is chosen afterwards.</param>
	/// <returns>A task that completes when the dialog has handled the change and rendered.</returns>
	public async Task SetPickedAsync(TransferSection section, bool picked)
	{
		await Host.SetCheckedAsync(await ChoiceAsync(section), picked);
	}

	/// <summary>Reads the problems that stop sections from being imported, as the dialog lists them under their sections.</summary>
	/// <returns>The problems, in document order.</returns>
	public async Task<IReadOnlyList<string>> ProblemsAsync()
	{
		IReadOnlyList<GateRenderedElement> elements = await Host.ElementsAsync();
		List<string> problems = [];
		bool inProblems = false;

		foreach (GateRenderedElement element in elements)
		{
			if (element.Name is "ul" or "li" && element.Attribute("class") is string cssClass)
			{
				inProblems = cssClass == "import-problems";
			}
			else if (inProblems && element.Name == "li")
			{
				problems.Add(element.Text);
			}
		}

		return problems;
	}

	/// <summary>Reads the lines that say what sections remove.</summary>
	/// <returns>The lines, in document order.</returns>
	public Task<IReadOnlyList<string>> RemovesAsync()
	{
		return Host.TextsWithClassAsync("import-removes");
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await Host.CloseAsync();
		await _services.DisposeAsync();
	}

	private static bool HasClass(GateRenderedElement element, string cssClass)
	{
		return (element.Attribute("class") ?? string.Empty).Split(' ').Contains(cssClass);
	}
}
