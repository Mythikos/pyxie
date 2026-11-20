using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.DependencyInjection;
using Pyxie.Data.Persistence;
using Pyxie.Data.Projects;
using Pyxie.Data.Services;
using Pyxie.Data.Transfer;
using Pyxie.Tests.Support;

using Xunit;

namespace Pyxie.Tests.Services;

public sealed class SettingsTransferServiceRegistrationTests
{
	[Fact]
	public async Task AddPyxieData_RegistersTheSettingsTransferServiceScopedOnTheOpenProject()
	{
		await using TestDatabase database = new();
		OpenProject openProject = StandInProject.Open();
		ServiceCollection services = new();
		services.AddSingleton(database.ContextFactory);
		services.AddSingleton<IProjectDbContextSource>(database.ContextSource);
		services.AddSingleton<TimeProvider>(database.Clock);
		services.AddSingleton(openProject);
		services.AddPyxieData();
		await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

		using IServiceScope first = provider.CreateScope();
		using IServiceScope second = provider.CreateScope();
		ISettingsTransferService service = first.ServiceProvider.GetRequiredService<ISettingsTransferService>();

		Assert.IsType<SettingsTransferService>(service);
		Assert.Same(service, first.ServiceProvider.GetRequiredService<ISettingsTransferService>());
		Assert.NotSame(service, second.ServiceProvider.GetRequiredService<ISettingsTransferService>());
		Assert.Same(openProject, provider.GetRequiredService<OpenProject>());
		ProjectSettings settings = ResultAssert.Succeeded(await service.ExportAsync(SettingsTransferServiceTestData.ProjectId, TestContext.Current.CancellationToken));
		Assert.Equal(["Bug", "Chore", "Feature"], settings.Categories?.Select(category => category.Name) ?? []);
	}
}
