using System.Runtime.CompilerServices;

using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;
using Pyxie.Web.Components.Board;
using Pyxie.Web.Components.Board.Models;

using Xunit;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The card dialog rendered on a test database, opened on a card or on a new card, with the card service it saves through.</summary>
internal sealed class CardDialog : IAsyncDisposable
{
	private readonly ComponentHarness _harness;
	private readonly StrongBox<int> _changes;

	private CardDialog(ComponentHarness harness, RenderedComponent<CardEditor> editor, CardService cards, StrongBox<int> changes)
	{
		_harness = harness;
		_changes = changes;
		Editor = editor;
		Cards = cards;
	}

	/// <summary>Gets the rendered dialog.</summary>
	public RenderedComponent<CardEditor> Editor { get; }

	/// <summary>Gets the card service the dialog saves through, which a test can also use to act as someone else.</summary>
	public CardService Cards { get; }

	/// <summary>Gets how many times the dialog announced that cards changed.</summary>
	public int Changes => Volatile.Read(ref _changes.Value);

	/// <summary>Renders the dialog and opens it.</summary>
	/// <param name="database">The database that holds the cards, categories, and agents.</param>
	/// <param name="cardId">The card to open, or <see langword="null"/> for a new card.</param>
	/// <returns>The open dialog.</returns>
	public static async Task<CardDialog> OpenAsync(TestDatabase database, int? cardId = null)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		CardService cards = new(database.ContextFactory, database.Clock);
		await using ProjectDbContext context = database.CreateContext();
		List<Category> categories = await context.Categories.AsNoTracking().ToListAsync(cancellationToken);
		List<Agent> agents = await context.Agents.AsNoTracking().ToListAsync(cancellationToken);
		WorkCard? card = cardId is int id ? ResultAssert.Succeeded(await cards.GetAsync(id, cancellationToken)) : null;
		ComponentHarness harness = new(services => services.AddSingleton<ICardService>(cards));
		StrongBox<int> changes = new();

		RenderedComponent<CardEditor> editor = await harness.RenderAsync<CardEditor>(new Dictionary<string, object>
		{
			[nameof(CardEditor.Categories)] = categories,
			[nameof(CardEditor.Agents)] = agents,
			[nameof(CardEditor.Changed)] = EventCallback.Factory.Create(new object(), () => { Interlocked.Increment(ref changes.Value); }),
		});

		await editor.RunAsync(instance => instance.OpenAsync(card));

		return new CardDialog(harness, editor, cards, changes);
	}

	/// <summary>Gets the form the person is typing into.</summary>
	/// <returns>The form, which the test edits as the person would.</returns>
	public CardFormModel Form()
	{
		return Editor.ReadField<CardFormModel>("_form");
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _harness.DisposeAsync();
	}
}
