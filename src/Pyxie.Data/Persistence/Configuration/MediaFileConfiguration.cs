using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;
using Pyxie.Data.Media;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps stored media and its ownership without putting bytes in the database.</summary>
public sealed class MediaFileConfiguration : IEntityTypeConfiguration<MediaFile>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<MediaFile> builder)
	{
		builder.HasKey(file => file.Id);
		builder.Property(file => file.Id).ValueGeneratedNever();
		builder.Property(file => file.Name).HasMaxLength(MediaLimits.MaxNameLength).IsRequired();
		builder.Property(file => file.Caption).HasMaxLength(MediaLimits.MaxCaptionLength);
		builder.Property(file => file.ContentType).HasMaxLength(100).IsRequired();
		builder.Property(file => file.Sha256).HasMaxLength(64).IsRequired();
		builder.Property(file => file.Commit).HasMaxLength(64);
		builder.Property(file => file.PublicationKey).HasMaxLength(512);
		builder.HasIndex(file => new { file.RoundId, file.PublicationKey }).IsUnique();
		builder.HasOne<WorkCard>().WithMany(card => card.MediaFiles).HasForeignKey(file => file.CardId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne<CardRound>().WithMany().HasForeignKey(file => file.RoundId).OnDelete(DeleteBehavior.Cascade);
		builder.HasOne<MediaUpload>().WithMany().HasForeignKey(file => file.UploadId).OnDelete(DeleteBehavior.Cascade);
	}
}
