using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class FormSourceContentConfiguration : IEntityTypeConfiguration<FormSourceContent>
{
    public void Configure(EntityTypeBuilder<FormSourceContent> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.SourceType)
            .IsRequired();

        builder.Property(u => u.Content)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(u => u.FileName)
        .IsRequired(false)
        .HasMaxLength(512);

        builder.Property(u => u.Order)
            .IsRequired();
    }
}