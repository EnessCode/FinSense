using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class RecurringTransactionConfiguration : IEntityTypeConfiguration<RecurringTransaction>
{
    public void Configure(EntityTypeBuilder<RecurringTransaction> builder)
    {
        builder.ToTable("recurring_transactions", t =>
        {
            t.HasCheckConstraint("CK_recurring_transactions_amount_positive", "amount > 0");
            t.HasCheckConstraint("CK_recurring_transactions_day_of_month_range", "day_of_month BETWEEN 1 AND 31");
            t.HasCheckConstraint("CK_recurring_transactions_type_valid", "type IN (1, 2)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Type)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(r => r.Amount)
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(r => r.Title)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(r => r.DayOfMonth)
            .IsRequired();

        builder.Property(r => r.TotalOccurrences);

        builder.Property(r => r.CompletedOccurrences)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.StartDateUtc)
            .IsRequired();

        builder.Property(r => r.NextExecutionDateUtc)
            .IsRequired();

        builder.Property(r => r.LastExecutedAtUtc);

        builder.Property(r => r.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(r => r.Workspace)
            .WithMany()
            .HasForeignKey(r => r.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Wallet)
            .WithMany()
            .HasForeignKey(r => r.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.WorkspaceId, r.IsActive, r.NextExecutionDateUtc })
            .HasDatabaseName("IX_recurring_transactions_workspace_active_next");
    }
}
