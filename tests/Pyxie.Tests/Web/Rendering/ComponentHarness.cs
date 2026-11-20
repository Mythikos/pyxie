using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>Renders the application's components to HTML on the server, with Blazorise and the Ant Design provider registered as in the application, so tests can check the markup a component produces for a state without a browser. Nothing that runs after the first render (JavaScript, focus, event handlers raised by a click) takes part.</summary>
internal sealed class ComponentHarness : IAsyncDisposable
{
	private readonly ServiceProvider _provider;
	private readonly HtmlRenderer _renderer;

	/// <summary>Initializes a new instance of the <see cref="ComponentHarness"/> class.</summary>
	/// <param name="configure">Registers the services the component under test injects.</param>
	public ComponentHarness(Action<IServiceCollection> configure)
	{
		ServiceCollection services = new();
		services.AddLogging();
		services.AddSingleton<IJSRuntime, UnavailableJSRuntime>();
		services.AddSingleton<NavigationManager>(Navigation);
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(StandInProject.Open());
		services.AddSingleton<PipelineController>();
		configure(services);

		_provider = services.BuildServiceProvider();
		_renderer = new HtmlRenderer(_provider, _provider.GetRequiredService<ILoggerFactory>());
	}

	/// <summary>Gets the navigation manager components are given.</summary>
	public TestNavigationManager Navigation { get; } = new();

	/// <summary>Gets the services components are given.</summary>
	public IServiceProvider Services => _provider;

	/// <summary>Renders a component.</summary>
	/// <typeparam name="TComponent">The component type.</typeparam>
	/// <param name="parameters">The component parameters by name.</param>
	/// <returns>The rendered component.</returns>
	public async Task<RenderedComponent<TComponent>> RenderAsync<TComponent>(IReadOnlyDictionary<string, object>? parameters = null)
		where TComponent : IComponent
	{
		TComponent? instance = default;

		RenderFragment content = builder =>
		{
			builder.OpenComponent<TComponent>(0);
			builder.AddMultipleAttributes(1, parameters);
			builder.AddComponentReferenceCapture(2, reference => instance = (TComponent)reference);
			builder.CloseComponent();
		};

		HtmlRootComponent root = await _renderer.Dispatcher.InvokeAsync(() => _renderer.RenderComponentAsync<RenderHost>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(RenderHost.Content)] = content })));

		return new RenderedComponent<TComponent>(_renderer, root, instance ?? throw new InvalidOperationException($"{typeof(TComponent).Name} was not created."));
	}

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		await _renderer.DisposeAsync();
		await _provider.DisposeAsync();
	}
}
