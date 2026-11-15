using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Services;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class RepositoryServiceRegistrationTests
{
	[Fact]
	public async Task AddPyxieData_RegistersTheRepositoryServiceScoped()
	{
		await using TestDatabase database = new();
		ServiceCollection services = new();
		services.AddSingleton(database.ContextFactory);
		services.AddSingleton<TimeProvider>(database.Clock);
		services.AddPyxieData();
		await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		using IServiceScope first = provider.CreateScope();
		using IServiceScope second = provider.CreateScope();
		IRepositoryService service = first.ServiceProvider.GetRequiredService<IRepositoryService>();

		Assert.IsType<RepositoryService>(service);
		Assert.Same(service, first.ServiceProvider.GetRequiredService<IRepositoryService>());
		Assert.NotSame(service, second.ServiceProvider.GetRequiredService<IRepositoryService>());
		Assert.Null(await service.GetAsync(TestContext.Current.CancellationToken));
	}
}
