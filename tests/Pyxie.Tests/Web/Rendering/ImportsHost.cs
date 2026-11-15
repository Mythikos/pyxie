using Microsoft.Extensions.DependencyInjection;

using Pyxie.Data.Services;
using Pyxie.Runtime.Projects;
using Pyxie.Runtime.Repositories;
using Pyxie.Tests.Runtime.Repositories;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The background imports of the application: a <see cref="RepositoryImports"/> that runs the real <see cref="ProjectSetupService"/> of a <see cref="RepositoryRig"/> in a scope of its own, with the real git client, in a workspace root of the rig's own, or over a rig the test built. Disposing it cancels an import still running, waits for it, and disposes the rig it made.</summary>
internal sealed class ImportsHost : IAsyncDisposable
{
	private readonly ServiceProvider _scopes;
	private readonly RepositoryRig? _ownedRig;

	/// <summary>Initializes a new instance of the <see cref="ImportsHost"/> class over a rig of its own.</summary>
	/// <param name="catalog">A project catalog to use instead of the real one, for a test that holds an import at the catalog.</param>
	public ImportsHost(IProjectCatalog? catalog = null)
		: this(new RepositoryRig(catalog: catalog), ownsRig: true)
	{
	}

	/// <summary>Initializes a new instance of the <see cref="ImportsHost"/> class over a rig the test already built, which the test disposes.</summary>
	/// <param name="rig">The rig whose setup service the imports run.</param>
	public ImportsHost(RepositoryRig rig)
		: this(rig, ownsRig: false)
	{
	}

	private ImportsHost(RepositoryRig rig, bool ownsRig)
	{
		_ownedRig = ownsRig ? rig : null;
		Setup = rig.Setup;
		Sessions = rig.Sessions;
		_scopes = new ServiceCollection().AddScoped(_ => Setup).BuildServiceProvider();
		Imports = new RepositoryImports(_scopes.GetRequiredService<IServiceScopeFactory>());
	}

	/// <summary>Gets the setup service the imports run.</summary>
	public ProjectSetupService Setup { get; }

	/// <summary>Gets the service that opens and closes projects, which a component that closes the project uses.</summary>
	public ProjectSessions Sessions { get; }

	/// <summary>Gets the imports that run in the background.</summary>
	public RepositoryImports Imports { get; }

	/// <inheritdoc/>
	public async ValueTask DisposeAsync()
	{
		Imports.Dispose();
		await Imports.Running;
		await _scopes.DisposeAsync();

		if (_ownedRig is not null)
		{
			await _ownedRig.DisposeAsync();
		}
	}
}
