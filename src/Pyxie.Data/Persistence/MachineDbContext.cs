using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Entities;
using Pyxie.Data.Persistence.Configuration.Machine;
using Pyxie.Data.Persistence.Converters;

namespace Pyxie.Data.Persistence;

/// <summary>The provider-agnostic EF Core model of the machine database in the workspace root: the projects Pyxie knows about and which one is open.</summary>
public class MachineDbContext : DbContext
{
	/// <summary>Initializes a new instance of the <see cref="MachineDbContext"/> class.</summary>
	/// <param name="options">The provider-specific options.</param>
	public MachineDbContext(DbContextOptions<MachineDbContext> options)
		: base(options)
	{
	}

	/// <summary>Gets the projects.</summary>
	public DbSet<Project> Projects => Set<Project>();

	/// <inheritdoc/>
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// The project database's configurations live in their own namespace and must stay out of this model.
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(MachineDbContext).Assembly, type => type.Namespace == typeof(ProjectConfiguration).Namespace);
	}

	/// <inheritdoc/>
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
	}
}
