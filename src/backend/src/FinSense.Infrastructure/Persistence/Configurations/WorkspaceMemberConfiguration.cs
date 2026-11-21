using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");

        builder.HasKey(wm => wm.Id);

        builder.Property(wm => wm.Role)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(wm => wm.JoinedAtUtc)
            .IsRequired();

        builder.HasOne(wm => wm.Workspace)
            .WithMany(w => w.Members)
            .HasForeignKey(wm => wm.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(wm => wm.User)
            .WithMany(u => u.WorkspaceMemberships)
            .HasForeignKey(wm => wm.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Mandatory index #4: O(1) lookup for Workspace Authorization Filter
        builder.HasIndex(wm => new { wm.WorkspaceId, wm.UserId })
            .HasDatabaseName("IX_workspace_members_workspace_user")
            .IsUnique();
    }
}
