using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class AiConversationConfiguration : IEntityTypeConfiguration<AiConversation>
{
    public void Configure(EntityTypeBuilder<AiConversation> builder)
    {
        builder.ToTable("ai_conversations");

        builder.HasKey(ac => ac.Id);

        builder.Property(ac => ac.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(ac => ac.CreatedAtUtc)
            .IsRequired();

        builder.Property(ac => ac.UpdatedAtUtc)
            .IsRequired();

        builder.HasOne(ac => ac.Workspace)
            .WithMany(ws => ws.AiConversations)
            .HasForeignKey(ac => ac.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ac => ac.User)
            .WithMany()
            .HasForeignKey(ac => ac.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(AiConversation.Messages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
