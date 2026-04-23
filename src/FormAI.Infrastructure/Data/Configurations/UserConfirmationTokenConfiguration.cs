using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class UserConfirmationTokenConfiguration : IEntityTypeConfiguration<UserConfirmationToken>
{
    public void Configure(EntityTypeBuilder<UserConfirmationToken> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.TokenHash)
            .IsRequired()
            .HasMaxLength(64);
        
        builder.HasIndex(u => u.TokenHash).IsUnique();

        builder.Property(u => u.Purpose)
            .IsRequired();

        builder.Property(u => u.ExpiresAt)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .IsRequired();
    }
}