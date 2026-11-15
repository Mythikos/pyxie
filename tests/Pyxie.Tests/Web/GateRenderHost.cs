using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Web;

/// <summary>Builds the services the gate, agent, and category components run with, and renders them to static HTML. Static HTML shows what a component renders for the stored rows; <see cref="GateInteractiveHost"/> presses buttons.</summary>
public static class GateRenderHost
{
	/// <summary>Creates the services of a component: the real gate and agent services on a test database, and Blazorise registered the way the application registers it.</summary>
	/// <param name="database">The database the services read and write.</param>
	/// <param name="agentService">The agent service to use instead of the real one, or <see langword="null"/> for the real one.</param>
	/// <param name="jsRuntime">The script runtime to use instead of the one that does nothing, such as one that records what a component asks the browser to do, or <see langword="null"/> for the one that does nothing.</param>
	/// <returns>The service provider; the caller disposes it.</returns>
	public static ServiceProvider CreateServices(TestDatabase database, IAgentService? agentService = null, IJSRuntime? jsRuntime = null)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton(jsRuntime ?? new GateStubJsRuntime());
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(StandInProject.Open());
		services.AddSingleton(new PipelineActivity());
		services.AddSingleton<IGateService>(new GateService(database.ContextFactory));
		services.AddSingleton<IAgentService>(agentService ?? new AgentService(database.ContextFactory, database.Clock));
		services.AddSingleton<ICategoryService>(new CategoryService(database.ContextFactory));

		return services.BuildServiceProvider();
	}

	/// <summary>Renders a component and returns its HTML.</summary>
	/// <typeparam name="TComponent">The component to render.</typeparam>
	/// <param name="database">The database the gate service reads.</param>
	/// <param name="parameters">The component parameters by name, or <see langword="null"/> for none.</param>
	/// <returns>The rendered HTML, including the modals the component contains, which Blazorise renders hidden.</returns>
	public static async Task<string> RenderAsync<TComponent>(TestDatabase database, IReadOnlyDictionary<string, object?>? parameters = null)
		where TComponent : IComponent
	{
		await using ServiceProvider provider = CreateServices(database);
		await using HtmlRenderer renderer = new(provider, NullLoggerFactory.Instance);

		ParameterView view = ParameterView.FromDictionary(parameters is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(parameters));

		return await renderer.Dispatcher.InvokeAsync(async () =>
		{
			var root = await renderer.RenderComponentAsync<TComponent>(view);

			return root.ToHtmlString();
		});
	}
}
