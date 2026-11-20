using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services;
using Pyxie.Data.Services.Results;

namespace Pyxie.Tests.Web.Rendering;

/// <summary>The category service with its creates, renames, and color changes counted and holdable by a test; every operation does what the real service does.</summary>
internal sealed class GatedCategoryService : ICategoryService
{
	private readonly ICategoryService _inner;

	/// <summary>Initializes a new instance of the <see cref="GatedCategoryService"/> class.</summary>
	/// <param name="inner">The service that does the work.</param>
	public GatedCategoryService(ICategoryService inner)
	{
		_inner = inner;
	}

	/// <summary>Gets the probe that counts and holds the creates of categories.</summary>
	public CallProbe Creates { get; } = new();

	/// <summary>Gets the probe that counts and holds the renames of categories.</summary>
	public CallProbe Renames { get; } = new();

	/// <summary>Gets the probe that counts and holds the color changes of categories.</summary>
	public CallProbe Recolors { get; } = new();

	/// <inheritdoc/>
	public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
	{
		return _inner.ListAsync(cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> CreateAsync(string name, HexColor color, CancellationToken cancellationToken)
	{
		await Creates.EnterAsync(cancellationToken);

		return await _inner.CreateAsync(name, color, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> RenameAsync(int id, string name, CancellationToken cancellationToken)
	{
		await Renames.EnterAsync(cancellationToken);

		return await _inner.RenameAsync(id, name, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> SetColorAsync(int id, HexColor color, CancellationToken cancellationToken)
	{
		await Recolors.EnterAsync(cancellationToken);

		return await _inner.SetColorAsync(id, color, cancellationToken);
	}

	/// <inheritdoc/>
	public async Task<ServiceResult<Category>> UpdateIfCurrentAsync(int id, string? name, HexColor? color, string? expectedRevision, CancellationToken cancellationToken)
	{
		if (name is not null)
		{
			await Renames.EnterAsync(cancellationToken);
		}

		if (color is not null)
		{
			await Recolors.EnterAsync(cancellationToken);
		}

		return await _inner.UpdateIfCurrentAsync(id, name, color, expectedRevision, cancellationToken);
	}

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken) => DeleteIfCurrentAsync(id, null, cancellationToken);

	/// <inheritdoc/>
	public Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken)
	{
		return _inner.DeleteIfCurrentAsync(id, expectedRevision, cancellationToken);
	}
}
