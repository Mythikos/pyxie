using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="Gate"/>.</summary>
public sealed class GateConfiguration : IEntityTypeConfiguration<Gate>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<Gate> builder)
	{
		builder.HasKey(gate => gate.Id);
		builder.Property(gate => gate.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);
		builder.Property(gate => gate.Command).IsRequired().HasMaxLength(PyxieLimits.MaxCommandLength);
		builder.Property(gate => gate.Arguments).IsRequired().HasMaxLength(PyxieLimits.MaxArgumentsLength);

		// Case-insensitive uniqueness is enforced by the service; the index guards exact duplicates on any provider.
		builder.HasIndex(gate => gate.Name).IsUnique();
	}
}
