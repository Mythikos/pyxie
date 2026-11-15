using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Services;
using Pyxie.Runtime.Orchestration;
using Pyxie.Tests.Support;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The services the board page runs with in an interactive host, where a test presses its buttons and sees what the page asked the browser to do: the real services over a test database, with an open stand-in project and the script runtime a test gives. The card service is a <see cref="GatedCardService"/>, so a test can hold the page's reads of the board.</summary>
internal static class BoardServices
{
	/// <summary>Creates the services of the board page.</summary>
	/// <param name="database">The database whose cards the board shows.</param>
	/// <param name="jsRuntime">The script runtime, such as one that records what the page asks the browser to do.</param>
	/// <returns>The service provider; the caller disposes it.</returns>
	public static ServiceProvider Create(TestDatabase database, IJSRuntime jsRuntime)
	{
		ServiceCollection services = new();
		services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
		services.AddSingleton(jsRuntime);
		services.AddBlazorise().AddAntDesignProviders().AddAntDesignIcons();
		services.AddSingleton(database.ContextFactory);
		services.AddSingleton<TimeProvider>(database.Clock);
		services.AddSingleton(StandInProject.Open());
		services.AddPyxieData();
		services.AddSingleton(new PipelineActivity());

		GatedCardService cards = new(new CardService(database.ContextFactory, database.Clock));
		services.AddSingleton(cards);
		services.AddSingleton<ICardService>(cards);

		return services.BuildServiceProvider();
	}
}
