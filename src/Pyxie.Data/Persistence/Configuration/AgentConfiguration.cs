using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="Agent"/>.</summary>
public sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<Agent> builder)
	{
		builder.HasKey(agent => agent.Id);
		builder.Property(agent => agent.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);
		builder.Property(agent => agent.Command).IsRequired().HasMaxLength(PyxieLimits.MaxCommandLength);
		builder.Property(agent => agent.Arguments).IsRequired().HasMaxLength(PyxieLimits.MaxArgumentsLength);

		// Case-insensitive uniqueness is enforced by the services; the index guards exact duplicates on any provider.
		builder.HasIndex(agent => agent.Name).IsUnique();

		// At most one merge agent is enforced by the services; the provider makes this index unique over the merge agent alone, since a filtered index needs provider SQL.
		builder.HasIndex(agent => agent.CanMerge);
	}
}
