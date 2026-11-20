using Pyxie.Data.Colors;
using Pyxie.Data.Entities;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Owns the rows seeded into every new database.</summary>
public static class PyxieSeedData
{
	/// <summary>The identifier of the seeded Feature category.</summary>
	public const int FeatureCategoryId = 1;

	/// <summary>The identifier of the seeded Bug category.</summary>
	public const int BugCategoryId = 2;

	/// <summary>The identifier of the seeded Chore category.</summary>
	public const int ChoreCategoryId = 3;

	/// <summary>Creates the default categories.</summary>
	/// <returns>New category instances with fixed identifiers.</returns>
	public static IReadOnlyList<Category> CreateDefaultCategories() =>
	[
		new Category { Id = FeatureCategoryId, Name = "Feature", Color = CategoryColors.Green },
		new Category { Id = BugCategoryId, Name = "Bug", Color = CategoryColors.Pink },
		new Category { Id = ChoreCategoryId, Name = "Chore", Color = CategoryColors.Neutral },
	];
}
