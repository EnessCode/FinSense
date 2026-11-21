using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class AiMessageConfiguration : IEntityTypeConfiguration<AiMessage>
{
    public void Configure(EntityTypeBuilder<AiMessage> builder)
    {
        builder.ToTable("ai_messages");

        builder.HasKey(am => am.Id);

        builder.Property(am => am.Role)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(am => am.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(am => am.ReferencedMetrics)
            .HasColumnType("jsonb");

        builder.Property(am => am.ProviderUsed)
            .HasMaxLength(50);

        builder.Property(am => am.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(am => am.Conversation)
            .WithMany(ac => ac.Messages)
            .HasForeignKey(am => am.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
