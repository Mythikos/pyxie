using Pyxie.Data.Enums;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Support;
using Pyxie.Tests.Web.Rendering;
using Pyxie.Web.Components.Repository;

using Xunit;

namespace Pyxie.Tests.Web;

public sealed class RepositoryConnectTests
{
	private const string ExampleUrl = "https://example.com/owner/app.git";
	private const string AlreadyConnected = "A project is open. Close it to import another.";

	[Fact]
	public async Task Render_OffersOneFormWithTheFolderWayChosenAndNothingBusyOrFailedBeforeAnythingRuns()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();

		Assert.Single(Forms(elements));
		GateRenderedElement group = Assert.Single(elements, element => element.Attribute("role") == "radiogroup");
		Assert.Equal("Import from", group.Attribute("aria-label"));
		Assert.Equal(["Folder", "URL"], elements.Where(element => ClassesOf(element).Contains("ant-segmented-item-label")).Select(element => element.Text));
		Assert.Equal(["true", null], Radios(elements).Select(radio => radio.Attribute("checked")));
		GateRenderedElement entry = Entry(elements);
		Assert.Equal(@"D:\src\my-app", entry.Attribute("placeholder"));
		Assert.False(IsDisabled(entry), "An idle form lets the person type.");
		Assert.Equal(["Folder"], LabelsFor(elements, entry));
		GateRenderedElement import = Assert.Single(Buttons(elements));
		Assert.Equal("submit", import.Attribute("type"));
		Assert.Equal("Import", import.Text);
		Assert.False(IsDisabled(import) || IsLoading(import), "An idle form lets the person start the import.");
		Assert.All(Radios(elements), radio => Assert.False(IsDisabled(radio), "Either way of importing can be chosen."));
		Assert.Empty(Errors(elements));
	}

	[Fact]
	public async Task Choose_UrlChangesTheLabelAndThePlaceholderAndEachWayKeepsWhatWasEnteredForIt()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), @"D:\src\kept");

		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		GateRenderedElement entry = Entry(elements);
		Assert.Equal("Url", Assert.Single(Radios(elements), radio => radio.Attribute("checked") == "true").Attribute("value"));
		Assert.Equal("https://example.com/app.git", entry.Attribute("placeholder"));
		Assert.Equal(["URL"], LabelsFor(elements, entry));
		Assert.True(string.IsNullOrEmpty(entry.Attribute("value")), "What was entered for the folder is not shown as a URL.");

		await card.Host.TypeAsync(entry, ExampleUrl);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[0], true);

		IReadOnlyList<GateRenderedElement> back = await card.Host.ElementsAsync();
		Assert.Equal(@"D:\src\kept", Entry(back).Attribute("value"));
		Assert.Equal(@"D:\src\my-app", Entry(back).Attribute("placeholder"));
		Assert.Equal(["Folder"], LabelsFor(back, Entry(back)));
	}

	[Fact]
	public async Task Submit_StartsACloneInTheBackgroundAndShowsTheFormBusy()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);

		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));

		Assert.Equal(new RepositoryImport(RepositorySource.Url, ExampleUrl, null), card.Imports.Imports.Current);
		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.True(IsDisabled(Entry(elements)), "Nothing can be typed while an import runs.");
		Assert.All(Radios(elements), radio => Assert.True(IsDisabled(radio), "The way of importing cannot change while an import runs."));
		GateRenderedElement import = Assert.Single(Buttons(elements));
		Assert.True(IsDisabled(import), "A second import cannot start beside the first.");
		Assert.True(IsLoading(import), "The Import button shows the import running.");
		Assert.Empty(Errors(elements));
	}

	[Fact]
	public async Task Submit_StartsAFolderImportInTheBackgroundAndShowsTheFormBusy()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), repository.Folder);

		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));

		Assert.Equal(new RepositoryImport(RepositorySource.Folder, repository.Folder, null), card.Imports.Imports.Current);
		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.True(IsLoading(Assert.Single(Buttons(elements))), "The Import button shows the import running.");
		Assert.Equal(repository.Folder, Entry(elements).Attribute("value"));
	}

	[Fact]
	public async Task Render_ShowsTheReasonAFolderImportFailedUnderTheForm()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), card.MissingFolder);

		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.Imports.Imports.Running;

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		GateRenderedElement error = Assert.Single(Errors(elements));
		Assert.Equal($"The folder {card.MissingFolder} does not exist.", error.Text);
		Assert.Equal("alert", error.Attribute("role"));
		Assert.True(elements.ToList().IndexOf(error) > elements.ToList().IndexOf(Entry(elements)), "The error belongs under the form's field.");
		Assert.False(IsDisabled(Entry(elements)), "The person can correct the entry once the import failed.");
		Assert.Equal(card.MissingFolder, Entry(elements).Attribute("value"));
	}

	[Fact]
	public async Task Render_ShowsTheReasonACloneFailedAndTheUrlWayItWasTried()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);
		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));

		await card.FinishHeldImportAsync();

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.Equal(AlreadyConnected, Assert.Single(Errors(elements)).Text);
		Assert.Equal(ExampleUrl, Entry(elements).Attribute("value"));
		Assert.Equal(["URL"], LabelsFor(elements, Entry(elements)));
		Assert.False(IsDisabled(Assert.Single(Buttons(elements))) || IsLoading(Assert.Single(Buttons(elements))), "The import can be tried again.");
	}

	[Fact]
	public async Task Submit_AfterAFailureStartsAgainAndTakesTheOldErrorAway()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);
		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.FinishHeldImportAsync();
		Assert.Single(Errors(await card.Host.ElementsAsync()));

		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));

		Assert.Equal(new RepositoryImport(RepositorySource.Url, ExampleUrl, null), card.Imports.Imports.Current);
		Assert.Empty(Errors(await card.Host.ElementsAsync()));
	}

	[Fact]
	public async Task Edit_OfTheFieldOfAFailedImportTakesItsErrorAway()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);
		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.FinishHeldImportAsync();

		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), $"{ExampleUrl}x");

		Assert.Null(card.Imports.Imports.Current);
		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.Empty(Errors(elements));
		Assert.Equal($"{ExampleUrl}x", Entry(elements).Attribute("value"));
	}

	[Fact]
	public async Task Choose_TheOtherWayKeepsTheErrorOfTheFailedImportForWhenThePersonComesBack()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);
		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.FinishHeldImportAsync();

		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[0], true);

		Assert.NotNull(card.Imports.Imports.Current);
		Assert.Empty(Errors(await card.Host.ElementsAsync()));

		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);

		Assert.Equal(AlreadyConnected, Assert.Single(Errors(await card.Host.ElementsAsync())).Text);
	}

	[Fact]
	public async Task Edit_OfTheOtherWaysFieldKeepsTheError()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[1], true);
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), ExampleUrl);
		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.FinishHeldImportAsync();
		await card.Host.SetCheckedAsync(Radios(await card.Host.ElementsAsync())[0], true);

		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), card.MissingFolder);

		Assert.NotNull(card.Imports.Imports.Current);
	}

	[Fact]
	public async Task Open_WhileAnImportRunsShowsItBusyWithWhatWasEntered()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		card.Imports.Imports.TryStart(RepositorySource.Url, ExampleUrl);

		await using GateInteractiveHost later = card.OpenAnother();
		await later.StartAsync<RepositoryConnect>();

		IReadOnlyList<GateRenderedElement> elements = await later.ElementsAsync();
		Assert.True(IsDisabled(Entry(elements)), "A form opened during an import waits for it.");
		Assert.Equal(ExampleUrl, Entry(elements).Attribute("value"));
		Assert.Equal(["URL"], LabelsFor(elements, Entry(elements)));
		Assert.True(IsLoading(Assert.Single(Buttons(elements))), "The import is shown running.");
	}

	[Fact]
	public async Task Open_AfterAnImportFailedShowsItsReason()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();
		card.Imports.Imports.TryStart(RepositorySource.Folder, card.MissingFolder);
		await card.Imports.Imports.Running;

		await using GateInteractiveHost later = card.OpenAnother();
		await later.StartAsync<RepositoryConnect>();

		IReadOnlyList<GateRenderedElement> elements = await later.ElementsAsync();
		Assert.Equal($"The folder {card.MissingFolder} does not exist.", Assert.Single(Errors(elements)).Text);
		Assert.Equal(card.MissingFolder, Entry(elements).Attribute("value"));
	}

	[Fact]
	public async Task Render_FollowsAnImportThatAnotherTabStarted()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);

		card.Imports.Imports.TryStart(RepositorySource.Url, ExampleUrl);

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.True(IsDisabled(Entry(elements)), "The form waits for the import the other tab started.");
		Assert.Equal(ExampleUrl, Entry(elements).Attribute("value"));
		Assert.Equal(["URL"], LabelsFor(elements, Entry(elements)));
		Assert.True(IsLoading(Assert.Single(Buttons(elements))), "The import is shown running.");
	}

	[Fact]
	public async Task Render_ClearsWhatWasEnteredOnceTheImportItFollowedSucceeded()
	{
		await using TempGitRepository repository = await TempGitRepository.CreateAsync();
		await using ConnectCard card = await ConnectCard.OpenOverRealImportsAsync();
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), repository.Folder);

		await card.Host.SubmitAsync(Assert.Single(Forms(await card.Host.ElementsAsync())));
		await card.Imports.Imports.Running;

		Assert.Null(card.Imports.Imports.Current);
		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();
		Assert.Empty(Errors(elements));
		Assert.False(Entry(elements).Attributes.TryGetValue("value", out string? text) && text.Length > 0, "What was entered became a project, so the form is ready for the next one.");
	}

	[Fact]
	public async Task Render_WaitsWhileAProjectIsOpenAndSaysWhyOnTheWrapperOfTheDisabledControls()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(projectOpen: true);

		IReadOnlyList<GateRenderedElement> elements = await card.Host.ElementsAsync();

		Assert.True(IsDisabled(Entry(elements)), "No project is imported while one is open.");
		GateRenderedElement import = Assert.Single(Buttons(elements));
		Assert.True(IsDisabled(import), "No project is imported while one is open.");
		Assert.False(IsLoading(import), "Nothing is running.");
		GateRenderedElement fields = Assert.Single(elements, element => ClassesOf(element).Contains("connect-fields"));
		Assert.Equal(AlreadyConnected, fields.Attribute("title"));
		Assert.Empty(Errors(elements));
	}

	[Fact]
	public async Task Submit_SentBeforeTheBrowserShowedAProjectOpen_StartsNothing()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();
		await card.Host.TypeAsync(Entry(await card.Host.ElementsAsync()), card.MissingFolder);
		GateRenderedElement form = Assert.Single(Forms(await card.Host.ElementsAsync()));
		await card.Host.HoldAcknowledgementsAsync();
		await card.Host.UpdateAsync(new Dictionary<string, object?> { [nameof(RepositoryConnect.ProjectOpen)] = true });

		await card.Host.SubmitAsync(form);
		await card.Host.AcknowledgeAsync();

		Assert.Null(card.Imports.Imports.Current);
		Assert.Empty(Errors(await card.Host.ElementsAsync()));
	}

	[Fact]
	public async Task Render_GivesNoReasonWhileNoProjectIsOpen()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync();

		GateRenderedElement fields = Assert.Single(await card.Host.ElementsAsync(), element => ClassesOf(element).Contains("connect-fields"));

		Assert.Null(fields.Attribute("title"));
	}

	[Fact]
	public async Task Dispose_DoesNotStopTheImportAndStopsListeningToIt()
	{
		await using ConnectCard card = await ConnectCard.OpenAsync(holdImports: true);
		card.Imports.Imports.TryStart(RepositorySource.Url, ExampleUrl);
		Assert.Equal(1, EventSubscribers.Count(card.Imports.Imports, nameof(RepositoryImports.Changed)));

		await card.Host.CloseAsync();
		Assert.Equal(0, EventSubscribers.Count(card.Imports.Imports, nameof(RepositoryImports.Changed)));
		await card.FinishHeldImportAsync();

		Assert.Equal(AlreadyConnected, card.Imports.Imports.Current?.Error);
	}

	// The field of the form: the one text input, the radios of the segmented control being inputs too.
	private static GateRenderedElement Entry(IReadOnlyList<GateRenderedElement> elements)
	{
		return Assert.Single(elements, element => element.Name == "input" && element.Attribute("type") == "text");
	}

	private static IReadOnlyList<GateRenderedElement> Radios(IReadOnlyList<GateRenderedElement> elements)
	{
		return [.. elements.Where(element => element.Name == "input" && element.Attribute("type") == "radio")];
	}

	private static IReadOnlyList<GateRenderedElement> Forms(IReadOnlyList<GateRenderedElement> elements)
	{
		return [.. elements.Where(element => element.Name == "form")];
	}

	private static IReadOnlyList<GateRenderedElement> Buttons(IReadOnlyList<GateRenderedElement> elements)
	{
		return [.. elements.Where(element => element.Name == "button")];
	}

	private static IReadOnlyList<GateRenderedElement> Errors(IReadOnlyList<GateRenderedElement> elements)
	{
		return [.. elements.Where(element => ClassesOf(element).Contains("connect-error"))];
	}

	// The text of every label that names a field, which is how a person knows what to enter.
	private static IReadOnlyList<string> LabelsFor(IReadOnlyList<GateRenderedElement> elements, GateRenderedElement field)
	{
		string? id = field.Attribute("id");

		return [.. elements.Where(element => element.Name == "label" && id is not null && element.Attribute("for") == id).Select(element => element.Text)];
	}

	private static bool IsDisabled(GateRenderedElement element)
	{
		return element.Attributes.ContainsKey("disabled");
	}

	private static bool IsLoading(GateRenderedElement button)
	{
		return ClassesOf(button).Contains("ant-btn-loading");
	}

	private static string[] ClassesOf(GateRenderedElement element)
	{
		return (element.Attribute("class") ?? string.Empty).Split(' ');
	}
}
