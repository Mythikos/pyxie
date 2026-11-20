using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Entities;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Pages;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The board page rendered on a test database, with the announcements of changed cards and project changes it listens to, the reads of the board a test can hold, and the open project, which a test may open and close; the test database stands in for whichever project is open.</summary>
internal sealed class BoardPage : IAsyncDisposable
{
	private BoardPage(ComponentHarness harness, RenderedComponent<Board> board, PipelineActivity activity, GatedCardService cards, OpenProject openProject)
	{
		Harness = harness;
		Board = board;
		Activity = activity;
		Cards = cards;
		OpenProject = openProject;
	}

	/// <summary>Gets the harness the page was rendered by.</summary>
	public ComponentHarness Harness { get; }

	/// <summary>Gets the rendered page.</summary>
	public RenderedComponent<Board> Board { get; }

	/// <summary>Gets the open project, whose board the page shows.</summary>
	public OpenProject OpenProject { get; }

	/// <summary>Gets the announcements of changed cards and project changes, which the page listens to.</summary>
	public PipelineActivity Activity { get; }

	/// <summary>Gets the card service of the page, whose reads of the board are counted.</summary>
	public GatedCardService Cards { get; }

	/// <summary>Renders the board page.</summary>
	/// <param name="database">The database whose cards the board shows.</param>
	/// <param name="openProject">The open project, or <see langword="null"/> for one with a stand-in project open.</param>
	/// <param name="time">The clock the page reads and sets its timer on, or <see langword="null"/> for the database's clock, which stands still and whose timers are real.</param>
	/// <returns>The rendered page.</returns>
	public static async Task<BoardPage> RenderAsync(TestDatabase database, OpenProject? openProject = null, TimeProvider? time = null)
	{
		OpenProject project = openProject ?? StandInProject.Open();
		PipelineActivity activity = new();
		GatedCardService cards = new(new CardService(database.ContextFactory, database.Clock));

		ComponentHarness harness = new(services =>
		{
			services.AddSingleton(database.ContextFactory);
			services.AddSingleton<Pyxie.Data.Persistence.IProjectDbContextSource>(database.ContextSource);
			services.AddSingleton(time ?? database.Clock);
			services.AddSingleton(project);
			services.AddPyxieData();
			services.AddSingleton(activity);
			services.AddSingleton<ICardService>(cards);
		});

		return new BoardPage(harness, await harness.RenderAsync<Board>(), activity, cards, project);
	}

	/// <summary>Does what a click on the Retry button of a card's tile does: runs the page's handler of that name with the card, and renders the page again afterwards, as the framework does after an event handler.</summary>
	/// <param name="card">The card whose tile was clicked.</param>
	/// <returns>A task that completes when the handler has, and the render after it.</returns>
	public Task RetryAsync(WorkCard card)
	{
		return ClickAsync("RetryAsync", card);
	}

	/// <summary>Does what a click on the Close button of a card's tile does: runs the page's handler of that name with the card, and renders the page again afterwards.</summary>
	/// <param name="card">The card whose tile was clicked.</param>
	/// <returns>A task that completes when the handler has, and the render after it.</returns>
	public Task CloseAsync(WorkCard card)
	{
		return ClickAsync("CloseAsync", card);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return Harness.DisposeAsync();
	}

	private Task ClickAsync(string handler, WorkCard card)
	{
		MethodInfo method = typeof(Board).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public) ?? throw new MissingMethodException(typeof(Board).Name, handler);

		return Board.RunAsync(board => ((IHandleEvent)board).HandleEventAsync(new EventCallbackWorkItem(() => method.Invoke(board, [card, OpenProject.Current?.Id ?? 0]) as Task ?? Task.CompletedTask), null));
	}
}
