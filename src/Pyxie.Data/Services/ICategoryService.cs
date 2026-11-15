using Pyxie.Data.Entities;
using Pyxie.Data.Enums;
using Pyxie.Data.Services.Results;

namespace Pyxie.Data.Services;

/// <summary>Manages the list of categories available to cards.</summary>
public interface ICategoryService
{
	/// <summary>Gets all categories ordered by name.</summary>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The categories.</returns>
	Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);

	/// <summary>Creates a category.</summary>
	/// <param name="name">The unique, case-insensitive name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The created category, or a failure describing why the name was rejected.</returns>
	Task<ServiceResult<Category>> CreateAsync(string name, CancellationToken cancellationToken);

	/// <summary>Renames a category.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="name">The new unique, case-insensitive name.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The renamed category, or a failure describing why the change was rejected.</returns>
	Task<ServiceResult<Category>> RenameAsync(int id, string name, CancellationToken cancellationToken);

	/// <summary>Sets the tint of a category's chip. Choosing the color the category already has succeeds.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="color">The new color.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>The updated category, or a failure when the color is not a defined <see cref="CategoryColor"/> or the category is missing. A new category is <see cref="CategoryColor.Neutral"/> until its color is set.</returns>
	Task<ServiceResult<Category>> SetColorAsync(int id, CategoryColor color, CancellationToken cancellationToken);

	/// <summary>Deletes a category that no card references.</summary>
	/// <param name="id">The category identifier.</param>
	/// <param name="cancellationToken">A token to cancel the operation.</param>
	/// <returns>A failure when the category is missing or still referenced by cards.</returns>
	Task<ServiceResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
