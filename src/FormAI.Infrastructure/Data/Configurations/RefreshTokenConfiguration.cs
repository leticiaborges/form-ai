using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Token)
            .HasMaxLength(1024)
            .IsRequired(true);

        builder.HasIndex(s => s.Token)
            .IsUnique();

        builder.Property(s => s.ExpiresAt)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .IsRequired(true);

        builder.Property(s => s.RevokedAt)
            .IsRequired(false);

        builder.Property(s => s.ReplacedByToken)
            .HasMaxLength(1024)
            .IsRequired(false);
    }
}