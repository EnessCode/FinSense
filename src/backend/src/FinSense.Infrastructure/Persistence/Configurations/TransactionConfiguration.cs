using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", t =>
        {
            t.HasCheckConstraint("CK_transactions_amount_positive", "amount > 0");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(t => t.CurrencyCode)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(t => t.OriginalAmount)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(t => t.OriginalCurrencyCode)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength()
            .IsRequired();

        builder.Property(t => t.ExchangeRate)
            .HasColumnType("numeric(19,8)")
            .HasPrecision(19, 8)
            .HasDefaultValue(1.00000000m)
            .IsRequired();

        builder.Property(t => t.TransactionDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(t => t.MerchantName)
            .HasMaxLength(200);

        builder.Property(t => t.Notes)
            .HasMaxLength(1000);

        builder.Property(t => t.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(t => t.DeletedAtUtc);

        builder.Property(t => t.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(t => t.Workspace)
            .WithMany(ws => ws.Transactions)
            .HasForeignKey(t => t.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.Wallet)
            .WithMany()
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.CounterpartyWallet)
            .WithMany()
            .HasForeignKey(t => t.CounterpartyWalletId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Receipt)
            .WithOne(r => r.Transaction)
            .HasForeignKey<Transaction>(t => t.ReceiptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.PaidByUser)
            .WithMany()
            .HasForeignKey(t => t.PaidByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Mandatory index #1: Transaction feed, monthly cash flow summaries, and date-range analytics
        builder.HasIndex(t => new { t.WorkspaceId, t.TransactionDate, t.Type })
            .HasDatabaseName("IX_transactions_workspace_date_type")
            .IsDescending(false, true, false)
            .HasFilter("is_deleted = FALSE");

        // Mandatory index #2: Real-time budget utilization and category pie charts
        builder.HasIndex(t => new { t.WorkspaceId, t.CategoryId, t.TransactionDate })
            .HasDatabaseName("IX_transactions_workspace_category_date")
            .IsDescending(false, false, true)
            .HasFilter("is_deleted = FALSE AND type = 2");

        // Mandatory index #3: Prevents duplicate transactions on mobile network retries
        builder.HasIndex(t => new { t.WorkspaceId, t.IdempotencyKey })
            .HasDatabaseName("IX_transactions_workspace_idempotency")
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");
    }
}
