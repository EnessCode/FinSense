using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class InvestmentTransactionConfiguration : IEntityTypeConfiguration<InvestmentTransaction>
{
    public void Configure(EntityTypeBuilder<InvestmentTransaction> builder)
    {
        builder.ToTable("investment_transactions", t =>
        {
            t.HasCheckConstraint("CK_investment_transactions_quantity_positive", "quantity > 0");
            t.HasCheckConstraint("CK_investment_transactions_unit_price_positive", "unit_price > 0");
            t.HasCheckConstraint("CK_investment_transactions_total_cost_positive", "total_cost > 0");
            t.HasCheckConstraint("CK_investment_transactions_current_unit_price_positive", "current_unit_price > 0");
            t.HasCheckConstraint("CK_investment_transactions_category_valid", "category BETWEEN 1 AND 5");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Category)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(i => i.Symbol)
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(i => i.AssetName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnType("numeric(18,6)")
            .HasPrecision(18, 6)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasColumnType("numeric(18,4)")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.TotalCost)
            .HasColumnType("numeric(18,2)")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.CurrentUnitPrice)
            .HasColumnType("numeric(18,4)")
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(i => i.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(i => i.PurchaseDateUtc)
            .IsRequired();

        builder.Property(i => i.Notes)
            .HasMaxLength(250);

        builder.Property(i => i.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(i => i.Workspace)
            .WithMany()
            .HasForeignKey(i => i.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.CreatedByUser)
            .WithMany()
            .HasForeignKey(i => i.CreatedByUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.WorkspaceId, i.Symbol })
            .HasDatabaseName("IX_investment_transactions_workspace_symbol");

        builder.HasIndex(i => new { i.WorkspaceId, i.PurchaseDateUtc })
            .HasDatabaseName("IX_investment_transactions_workspace_date")
            .IsDescending(false, true);
    }
}
