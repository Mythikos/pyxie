using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Limits;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps <see cref="CardRound"/> and its <see cref="AgentSnapshot"/>, which is stored in the round's own row.</summary>
public sealed class CardRoundConfiguration : IEntityTypeConfiguration<CardRound>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<CardRound> builder)
	{
		builder.HasKey(round => round.Id);
		builder.HasIndex(round => new { round.CardId, round.Sequence }).IsUnique();
		builder.Property(round => round.Briefing).HasMaxLength(PyxieLimits.MaxBriefingLength);
		builder.Property(round => round.Note).HasMaxLength(PyxieLimits.MaxRoundNoteLength);
		builder.Property(round => round.Report).HasMaxLength(PyxieLimits.MaxRoundReportLength);

		// An optional dependent sharing the round's row: a required Name tells EF whether a snapshot exists, so a round without an agent reads back as null.
		builder.OwnsOne(
			round => round.Agent,
			agent =>
			{
				agent.Property(snapshot => snapshot.Name).IsRequired().HasMaxLength(PyxieLimits.MaxNameLength);
				agent.Property(snapshot => snapshot.Command).IsRequired().HasMaxLength(PyxieLimits.MaxCommandLength);
				agent.Property(snapshot => snapshot.Arguments).IsRequired().HasMaxLength(PyxieLimits.MaxArgumentsLength);
			});
	}
}
