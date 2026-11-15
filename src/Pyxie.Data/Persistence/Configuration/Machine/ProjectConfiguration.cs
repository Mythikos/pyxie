using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration.Machine;

/// <summary>Maps <see cref="Project"/>. The index on <see cref="Project.IsOpen"/> is made unique for open projects only by the provider, because a filtered index needs the provider's own SQL (see <c>Pyxie.Data.Sqlite</c>); the catalog's conditional writes keep at most one project open on any provider.</summary>
public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<Project> builder)
	{
		builder.HasKey(project => project.Id);
		builder.Property(project => project.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);
		builder.Property(project => project.Folder).IsRequired().HasMaxLength(PyxieLimits.MaxPathLength);
		builder.Property(project => project.Source).IsRequired().HasMaxLength(PyxieLimits.MaxOriginLength);

		// Case-insensitive uniqueness is enforced by the catalog; the indexes guard exact duplicates on any provider.
		builder.HasIndex(project => project.Name).IsUnique();
		builder.HasIndex(project => project.Folder).IsUnique();
		builder.HasIndex(project => project.IsOpen);
	}
}
