using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Configures the <see cref="SandboxSettings"/> table, which holds at most one row.</summary>
public sealed class SandboxSettingsConfiguration : IEntityTypeConfiguration<SandboxSettings>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<SandboxSettings> builder)
	{
		builder.HasKey(settings => settings.Id);
		builder.Property(settings => settings.Dockerfile).IsRequired().HasMaxLength(PyxieLimits.MaxDockerfileLength);
	}
}
