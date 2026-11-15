using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Runtime.Repositories;

/// <summary>A repository service that has nothing connected and refuses every connection, so a test can drive an import past its clone, its database, and its opened project into the refusal. It records the hub it was asked to connect.</summary>
public sealed class RefusingRepositoryService : IRepositoryService
{
	/// <summary>The reason this service gives for refusing.</summary>
	public const string Reason = "The test refuses every connection.";

	/// <summary>Gets the input of the connection it was asked for, or <see langword="null"/> before it was asked.</summary>
	public RepositoryInput? Asked { get; private set; }

	/// <summary>Gets a value indicating whether the clone's folder existed on disk when it was asked, which proves the import cloned before it was refused.</summary>
	public bool CloneExistedWhenAsked { get; private set; }

	/// <inheritdoc/>
	public Task<Repository?> GetAsync(CancellationToken cancellationToken)
	{
		return Task.FromResult<Repository?>(null);
	}

	/// <inheritdoc/>
	public Task<ServiceResult<Repository>> ConnectAsync(RepositoryInput input, CancellationToken cancellationToken)
	{
		Asked = input;
		CloneExistedWhenAsked = Directory.Exists(input.Path);

		return Task.FromResult(ServiceResult<Repository>.Failure(Reason));
	}
}
