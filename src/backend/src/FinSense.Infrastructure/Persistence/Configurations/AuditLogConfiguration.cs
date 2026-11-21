using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(al => al.Id);

        builder.Property(al => al.ActionType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(al => al.EntityName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(al => al.EntityId)
            .IsRequired();

        builder.Property(al => al.OldValues)
            .HasColumnType("jsonb");

        builder.Property(al => al.NewValues)
            .HasColumnType("jsonb");

        builder.Property(al => al.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(al => al.Workspace)
            .WithMany(ws => ws.AuditLogs)
            .HasForeignKey(al => al.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(al => al.ActorUser)
            .WithMany()
            .HasForeignKey(al => al.ActorUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
