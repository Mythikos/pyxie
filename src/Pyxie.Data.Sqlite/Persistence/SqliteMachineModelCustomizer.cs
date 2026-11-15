using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Pyxie.Data.Entities;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Adds what the machine model needs from SQLite: the index on <see cref="Project.IsOpen"/> becomes unique among open projects, so the database itself refuses a second open project. A filtered index is written in the provider's SQL, which the provider-agnostic model in <c>Pyxie.Data</c> must not contain.</summary>
internal sealed class SqliteMachineModelCustomizer : RelationalModelCustomizer
{
	/// <summary>Initializes a new instance of the <see cref="SqliteMachineModelCustomizer"/> class.</summary>
	/// <param name="dependencies">The model customizer's dependencies, supplied by EF Core.</param>
	public SqliteMachineModelCustomizer(ModelCustomizerDependencies dependencies)
		: base(dependencies)
	{
	}

	/// <inheritdoc/>
	public override void Customize(ModelBuilder modelBuilder, DbContext context)
	{
		base.Customize(modelBuilder, context);
		modelBuilder.Entity<Project>().HasIndex(project => project.IsOpen).IsUnique().HasFilter("\"IsOpen\" = 1");
	}
}
