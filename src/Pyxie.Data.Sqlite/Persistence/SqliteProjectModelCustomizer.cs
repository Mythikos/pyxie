using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;

namespace Pyxie.Data.Sqlite.Persistence;

/// <summary>Adds what the project model needs from SQLite, written in the provider's SQL, which the provider-agnostic model in <c>Pyxie.Data</c> must not contain. The index on <see cref="Agent.CanMerge"/> becomes unique among merge agents, so the database itself refuses a second merge agent. A category's color must be written as <see cref="HexColor"/> writes it, so a row that something other than Pyxie wrote cannot stop the categories from being read.</summary>
internal sealed class SqliteProjectModelCustomizer : RelationalModelCustomizer
{
	/// <summary>Initializes a new instance of the <see cref="SqliteProjectModelCustomizer"/> class.</summary>
	/// <param name="dependencies">The model customizer's dependencies, supplied by EF Core.</param>
	public SqliteProjectModelCustomizer(ModelCustomizerDependencies dependencies)
		: base(dependencies)
	{
	}

	/// <inheritdoc/>
	public override void Customize(ModelBuilder modelBuilder, DbContext context)
	{
		base.Customize(modelBuilder, context);
		modelBuilder.Entity<Agent>().HasIndex(agent => agent.CanMerge).IsUnique().HasFilter("\"CanMerge\" = 1");

		// GLOB is case-sensitive and matches the whole value, so this admits exactly '#' and six upper-case hex digits.
		modelBuilder.Entity<Category>().ToTable(table => table.HasCheckConstraint("CK_Categories_Color", "\"Color\" GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'"));
	}
}
