using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Pyxie.Data.Persistence;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Creates a machine context for the EF Core command-line tools; it is not used at run time.</summary>
public sealed class MachineDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MachineDbContext>
{
	private const string DesignTimeConnectionString = "Data Source=pyxie.machine.design.db";

	/// <inheritdoc/>
	public MachineDbContext CreateDbContext(string[] args)
	{
		DbContextOptionsBuilder<MachineDbContext> builder = new();
		PyxieSqlite.ConfigureMachine(builder, DesignTimeConnectionString);

		return new MachineDbContext(builder.Options);
	}
}
