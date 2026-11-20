using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>An agent or gate editor rendered open to add an item, as its table shows it, with what it reports to the table through VisibleChanged recorded.</summary>
/// <typeparam name="TEditor">The editor.</typeparam>
internal sealed class EditorUnderTest<TEditor> : IAsyncDisposable
	where TEditor : IComponent
{
	private readonly ComponentHarness _harness;
	private readonly List<bool> _reported;

	private EditorUnderTest(ComponentHarness harness, RenderedComponent<TEditor> rendered, List<bool> reported)
	{
		_harness = harness;
		Rendered = rendered;
		_reported = reported;
	}

	/// <summary>Gets the rendered editor.</summary>
	public RenderedComponent<TEditor> Rendered { get; }

	/// <summary>Gets what the editor reported to the table, in order: <see langword="false"/> asks the table to hide it.</summary>
	public IReadOnlyList<bool> Reported => [.. _reported];

	/// <summary>Renders the editor open to add an item.</summary>
	/// <param name="configure">Registers the service the editor writes through.</param>
	/// <returns>The open editor.</returns>
	public static async Task<EditorUnderTest<TEditor>> OpenAsync(Action<IServiceCollection> configure)
	{
		ComponentHarness harness = new(configure);
		List<bool> reported = [];
		RenderedComponent<TEditor> rendered = await harness.RenderAsync<TEditor>(new Dictionary<string, object>
		{
			["Visible"] = true,
			["VisibleChanged"] = EventCallback.Factory.Create<bool>(new object(), reported.Add),
		});

		return new EditorUnderTest<TEditor>(harness, rendered, reported);
	}

	/// <inheritdoc/>
	public ValueTask DisposeAsync()
	{
		return _harness.DisposeAsync();
	}
}
