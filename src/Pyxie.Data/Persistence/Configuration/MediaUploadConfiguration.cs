using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Pyxie.Data.Entities;

namespace Pyxie.Data.Persistence.Configuration;

/// <summary>Maps expiring editor sessions.</summary>
public sealed class MediaUploadConfiguration : IEntityTypeConfiguration<MediaUpload>
{
	/// <inheritdoc/>
	public void Configure(EntityTypeBuilder<MediaUpload> builder)
	{
		builder.HasKey(upload => upload.Id);
		builder.Property(upload => upload.Id).ValueGeneratedNever();
		builder.HasOne<WorkCard>().WithMany().HasForeignKey(upload => upload.CardId).OnDelete(DeleteBehavior.Cascade);
	}
}
