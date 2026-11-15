using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="Repository"/>.</summary>
public sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<Repository> builder)
	{
		builder.HasKey(repository => repository.Id);
		builder.Property(repository => repository.Path).IsRequired().HasMaxLength(PyxieLimits.MaxPathLength);
		builder.Property(repository => repository.Origin).IsRequired().HasMaxLength(PyxieLimits.MaxOriginLength);
		builder.Property(repository => repository.TargetBranch).IsRequired().HasMaxLength(PyxieLimits.MaxBranchNameLength);
	}
}
