using FinSense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinSense.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.NameTr)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.NameEn)
            .HasMaxLength(100);

        builder.Property(c => c.Type)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(c => c.Icon)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.ColorHex)
            .HasMaxLength(9)
            .IsRequired();

        builder.Property(c => c.IsSystem)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(c => c.Workspace)
            .WithMany(ws => ws.Categories)
            .HasForeignKey(c => c.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
