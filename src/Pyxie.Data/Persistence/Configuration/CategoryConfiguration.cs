using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="Category"/> and seeds the default categories.</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<Category> builder)
	{
		builder.HasKey(category => category.Id);
		builder.Property(category => category.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);

		// Case-insensitive uniqueness is enforced by the services; the index guards exact duplicates on any provider.
		builder.HasIndex(category => category.Name).IsUnique();
		builder.HasData(PyxieSeedData.CreateDefaultCategories());
	}
}
