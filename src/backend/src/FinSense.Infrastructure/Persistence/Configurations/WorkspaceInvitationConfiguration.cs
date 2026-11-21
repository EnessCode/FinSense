using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class WorkspaceInvitationConfiguration : IEntityTypeConfiguration<WorkspaceInvitation>
{
    public void Configure(EntityTypeBuilder<WorkspaceInvitation> builder)
    {
        builder.ToTable("workspace_invitations");

        builder.HasKey(wi => wi.Id);

        builder.Property(wi => wi.InvitationCode)
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(wi => wi.InvitationCode)
            .IsUnique();

        builder.Property(wi => wi.InvitedEmail)
            .HasMaxLength(256);

        builder.Property(wi => wi.AssignedRole)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(wi => wi.ExpiresAtUtc)
            .IsRequired();

        builder.Property(wi => wi.AcceptedAtUtc);

        builder.Property(wi => wi.AcceptedByUserId);

        builder.HasOne(wi => wi.Workspace)
            .WithMany(w => w.Invitations)
            .HasForeignKey(wi => wi.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(wi => wi.InvitedByUser)
            .WithMany()
            .HasForeignKey(wi => wi.InvitedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
