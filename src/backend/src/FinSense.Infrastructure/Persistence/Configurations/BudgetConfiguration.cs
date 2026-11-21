using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets", t =>
        {
            t.HasCheckConstraint("CK_budgets_month_range", "month BETWEEN 1 AND 12");
            t.HasCheckConstraint("CK_budgets_limit_amount_positive", "limit_amount > 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Year)
            .IsRequired();

        builder.Property(b => b.Month)
            .IsRequired();

        builder.Property(b => b.LimitAmount)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(b => b.CurrencyCode)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(b => b.AlertThresholdPercent)
            .HasDefaultValue((short)80)
            .IsRequired();

        builder.Property(b => b.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // PostgreSQL xmin system column as optimistic concurrency token (EF Core 8 standard row version).
        // Equivalent to the obsolete UseXminAsConcurrencyToken(); produces an identical model (no migration diff).
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .IsRowVersion();

        builder.HasOne(b => b.Workspace)
            .WithMany(ws => ws.Budgets)
            .HasForeignKey(b => b.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mandatory index #5: Prevents duplicate active monthly budgets for the same category in a workspace
        builder.HasIndex(b => new { b.WorkspaceId, b.Year, b.Month, b.CategoryId })
            .HasDatabaseName("IX_budgets_workspace_year_month_category")
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter("is_deleted = FALSE");
    }
}
