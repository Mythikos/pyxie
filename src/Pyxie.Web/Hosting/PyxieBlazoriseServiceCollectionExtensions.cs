using Blazorise;
using Blazorise.AntDesign;
using Blazorise.Icons.AntDesign;

namespace Pyxie.Web.Hosting;

/// <summary>Registers Blazorise with the Ant Design provider and icons.</summary>
internal static class PyxieBlazoriseServiceCollectionExtensions
{
	private const string ProductTokenKey = "Blazorise:ProductToken";

	/// <summary>Registers Blazorise, reading the optional product token from <c>Blazorise:ProductToken</c>.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The application configuration.</param>
	/// <returns>The same collection, for chaining.</returns>
	public static IServiceCollection AddPyxieBlazorise(this IServiceCollection services, IConfiguration configuration)
	{
		string? productToken = configuration[ProductTokenKey];

		services
			.AddBlazorise(options =>
			{
				options.Immediate = true;

				if (!string.IsNullOrWhiteSpace(productToken))
				{
					options.ProductToken = productToken;
				}
			})
			.AddAntDesignProviders()
			.AddAntDesignIcons();

		return services;
	}
}
