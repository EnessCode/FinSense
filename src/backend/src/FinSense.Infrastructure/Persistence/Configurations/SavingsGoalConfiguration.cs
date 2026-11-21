using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class SavingsGoalConfiguration : IEntityTypeConfiguration<SavingsGoal>
{
    public void Configure(EntityTypeBuilder<SavingsGoal> builder)
    {
        builder.ToTable("savings_goals", t =>
        {
            t.HasCheckConstraint("CK_savings_goals_target_amount_positive", "target_amount > 0");
        });

        builder.HasKey(sg => sg.Id);

        builder.Property(sg => sg.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(sg => sg.TargetAmount)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(sg => sg.CurrentAmount)
            .HasColumnType("numeric(19,4)")
            .HasPrecision(19, 4)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(sg => sg.CurrencyCode)
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("TRY")
            .IsRequired();

        builder.Property(sg => sg.TargetDate)
            .HasColumnType("date");

        builder.Property(sg => sg.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(sg => sg.Workspace)
            .WithMany(ws => ws.SavingsGoals)
            .HasForeignKey(sg => sg.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sg => sg.LinkedWallet)
            .WithMany()
            .HasForeignKey(sg => sg.LinkedWalletId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
