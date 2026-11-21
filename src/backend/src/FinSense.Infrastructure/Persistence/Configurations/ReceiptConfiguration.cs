using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ObjectKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.HasIndex(r => r.ObjectKey)
            .IsUnique();

        builder.Property(r => r.ContentType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.FileSizeBytes)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(r => r.ExtractedMerchant)
            .HasMaxLength(200);

        builder.Property(r => r.ExtractedDate)
            .HasColumnType("date");

        builder.Property(r => r.ExtractedTotal)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4);

        builder.Property(r => r.ExtractedTax)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4);

        builder.Property(r => r.ConfidenceScore)
            .HasColumnType("numeric(5,4)")
            .HasPrecision(5, 4);

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(r => r.Workspace)
            .WithMany(ws => ws.Receipts)
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.UploadedByUser)
            .WithMany()
            .HasForeignKey(r => r.UploadedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
