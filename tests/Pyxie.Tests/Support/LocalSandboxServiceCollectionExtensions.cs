using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Pyxie.Runtime.Processes;
using Pyxie.Runtime.Sandboxes;

namespace Pyxie.Tests.Support;

/// <summary>Swaps the runtime's sandboxes for the local stand-ins, so tests run rounds without a container engine (see <see cref="LocalSandboxes"/>).</summary>
public static class LocalSandboxServiceCollectionExtensions
{
	/// <summary>Replaces <see cref="ISandboxes"/> with <see cref="LocalSandboxes"/> and <see cref="ISandboxSignIns"/> with <see cref="StandInSignIns"/>. Call it after <c>AddPyxieRuntime</c>.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="folder">The folder the sandboxes work in; the caller deletes it.</param>
	/// <returns>The same collection, for chaining.</returns>
	public static IServiceCollection AddLocalSandboxes(this IServiceCollection services, string folder)
	{
		services.Replace(ServiceDescriptor.Singleton<ISandboxes>(provider => new LocalSandboxes(provider.GetRequiredService<ProcessRunner>(), folder)));
		services.Replace(ServiceDescriptor.Singleton<ISandboxSignIns, StandInSignIns>());

		return services;
	}
}
