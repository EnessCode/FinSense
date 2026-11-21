using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("wallets");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(w => w.Type)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(w => w.CurrencyCode)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(w => w.InitialBalance)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(w => w.CurrentBalance)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(w => w.CreditLimit)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4);

        builder.Property(w => w.Icon)
            .HasMaxLength(50);

        builder.Property(w => w.ColorHex)
            .HasMaxLength(9);

        builder.Property(w => w.IsArchived)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(w => w.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // PostgreSQL xmin system column as optimistic concurrency token (EF Core 8 standard row version).
        // Equivalent to the obsolete UseXminAsConcurrencyToken(); produces an identical model (no migration diff).
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.HasOne(w => w.Workspace)
            .WithMany(ws => ws.Wallets)
            .HasForeignKey(w => w.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
