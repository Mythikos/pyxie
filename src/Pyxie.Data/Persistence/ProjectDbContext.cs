using Microsoft.EntityFrameworkCore;

using Pyxie.Data.Colors;
using Pyxie.Data.Entities;
using Pyxie.Data.Persistence.Configuration;
using Pyxie.Data.Persistence.Converters;

namespace Pyxie.Data.Persistence;

/// <summary>The provider-agnostic EF Core model of one project: its board, agents, categories, gates, and repository. Each project folder holds its own database.</summary>
public class ProjectDbContext : DbContext
{
	/// <summary>Initializes a new instance of the <see cref="ProjectDbContext"/> class.</summary>
	/// <param name="options">The provider-specific options.</param>
	public ProjectDbContext(DbContextOptions<ProjectDbContext> options)
		: base(options)
	{
	}

	/// <summary>Gets the card categories.</summary>
	public DbSet<Category> Categories => Set<Category>();

	/// <summary>Gets the agent definitions.</summary>
	public DbSet<Agent> Agents => Set<Agent>();

	/// <summary>Gets the board cards.</summary>
	public DbSet<WorkCard> Cards => Set<WorkCard>();

	/// <summary>Gets the pipeline rounds that make up each card's history.</summary>
	public DbSet<CardRound> Rounds => Set<CardRound>();

	/// <summary>Gets staged and retained media metadata.</summary>
	public DbSet<MediaFile> MediaFiles => Set<MediaFile>();

	/// <summary>Gets expiring card editor upload sessions.</summary>
	public DbSet<MediaUpload> MediaUploads => Set<MediaUpload>();

	/// <summary>Gets the files supplied to each round.</summary>
	public DbSet<RoundMediaInput> RoundMediaInputs => Set<RoundMediaInput>();

	/// <summary>Gets the automated checks gate rounds run.</summary>
	public DbSet<Gate> Gates => Set<Gate>();

	/// <summary>Gets the project's repository; there is exactly one once the project is imported.</summary>
	public DbSet<Repository> Repositories => Set<Repository>();

	/// <summary>Gets the project's sandbox settings: at most one row.</summary>
	public DbSet<SandboxSettings> SandboxSettings => Set<SandboxSettings>();

	/// <inheritdoc/>
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// The machine database's configurations live in their own namespace and must stay out of this model.
		modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProjectDbContext).Assembly, type => type.Namespace == typeof(CategoryConfiguration).Namespace);
	}

	/// <inheritdoc/>
	protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
	{
		// Also covers DateTime? properties such as WorkCard.ClosedUtc; UtcTimestampTests pins it.
		configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
		configurationBuilder.Properties<HexColor>().HaveConversion<HexColorConverter>().HaveMaxLength(HexColor.Length);
	}
}
