using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Services.Inputs;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Manages the list of categories available to cards.</summary>
public interface ICategoryService
{
	/// <summary>Gets all categories ordered by name.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The categories.</returns>
	Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

	/// <summary>Creates a category in a color.</summary>
	/// <param name="name">The unique, case-insensitive name.</param>
	/// <param name="color">The color of the category's chip.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The created category, or a failure describing why the name was rejected.</returns>
	Task<ServiceResult<Category>> CreateAsync(string name, HexColor color, CancellationToken cancellationToken);

	/// <summary>Renames a category.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="name">The new unique, case-insensitive name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The renamed category, or a failure describing why the change was rejected.</returns>
	Task<ServiceResult<Category>> RenameAsync(int id, string name, CancellationToken cancellationToken);

	/// <summary>Sets the color of a category's chip. Choosing the color the category already has succeeds.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="color">The new color.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The updated category, or a failure when the category is missing.</returns>
	Task<ServiceResult<Category>> SetColorAsync(int id, HexColor color, CancellationToken cancellationToken);

	/// <summary>Updates a category's name and color together while it matches the revision the caller read.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="name">The new name, or null to preserve it.</param>
	/// <param name="color">The new color, or null to preserve it.</param>
	/// <param name="expectedRevision">The revision from <see cref="SettingsRevision"/>, or null for an unconditional request.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The saved category, or the reason the edit was refused.</returns>
	Task<ServiceResult<Category>> UpdateIfCurrentAsync(int id, string? name, HexColor? color, string? expectedRevision, CancellationToken cancellationToken);

	/// <summary>Deletes a category that no card references.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the category is missing or still referenced by cards.</returns>
	Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken);

	/// <summary>Applies this operation only while the item still has the definition the caller read.</summary>
	/// <param name="id">The item identifier.</param>
	/// <param name="expectedRevision">The revision from <see cref="SettingsRevision"/>, or null for an unconditional request.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The operation result, including a refusal if the item changed.</returns>
	Task<ServiceResult> DeleteIfCurrentAsync(int id, string? expectedRevision, CancellationToken cancellationToken);
}
