using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps the immutable input manifest of each round.</summary>
public sealed class RoundMediaInputConfiguration : IEntityTypeConfiguration<RoundMediaInput>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<RoundMediaInput> builder)
	{
		builder.HasKey(input => new { input.RoundId, input.MediaFileId });
		builder.HasOne<CardRound>().WithMany(round => round.MediaInputs).HasForeignKey(input => input.RoundId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne(input => input.File).WithMany().HasForeignKey(input => input.MediaFileId).OnDelete(DeleteBehavior.Cascade);
	}
}
