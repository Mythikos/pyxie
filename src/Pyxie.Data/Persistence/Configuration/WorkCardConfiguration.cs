using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="WorkCard"/>.</summary>
public sealed class WorkCardConfiguration : IEntityTypeConfiguration<WorkCard>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<WorkCard> builder)
	{
		builder.HasKey(card => card.Id);
		builder.Property(card => card.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);
		builder.Property(card => card.Description).IsRequired().HasMaxLength(PyxieLimits.MaxDescriptionLength);
		builder.HasIndex(card => new { card.Lane, card.SortOrder });

		// Version is compared explicitly in conditional writes (see WorkCard.Version), not as an EF concurrency token, so renumbering SortOrder through tracked entities never conflicts.
		builder.Property(card => card.Version).IsRequired();

		builder.HasOne(card => card.Category).WithMany().HasForeignKey(card => card.CategoryId).IsRequired().OnDelete(DeleteBehavior.Restrict);

		// The property getter throws when the category was not loaded, so EF must read and write the backing field.
		builder.Navigation(card => card.Category).UsePropertyAccessMode(PropertyAccessMode.Field);
		builder.HasOne(card => card.WorkerAgent).WithMany().HasForeignKey(card => card.WorkerAgentId).OnDelete(DeleteBehavior.SetNull);
		builder.HasOne(card => card.ReviewerAgent).WithMany().HasForeignKey(card => card.ReviewerAgentId).OnDelete(DeleteBehavior.SetNull);

		// Rounds are the card's own history: they go with the card when a person deletes it from the Queue.
		builder.HasMany(card => card.Rounds).WithOne().HasForeignKey(round => round.CardId).IsRequired().OnDelete(DeleteBehavior.Cascade);
	}
}
