using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class FormConfiguration : IEntityTypeConfiguration<Form>
{
    public void Configure(EntityTypeBuilder<Form> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Title)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(u => u.Description)
            .IsRequired()
            .HasMaxLength(1024);

        builder.Property(u => u.SourceType)
            .IsRequired();

        builder.Property(u => u.IsPublic)
            .IsRequired();

        builder.Property(u => u.ExpiresAt)
            .IsRequired(false);

        builder.Property(u => u.ShowResultsAfterSubmit)
        .IsRequired();

        builder.Property(u => u.CreatedAt)
        .IsRequired();

        builder.HasMany(f => f.Questions)
            .WithOne(q => q.Form)
            .HasForeignKey(q => q.FormId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(f => f.Submissions)
            .WithOne(s => s.Form)
            .HasForeignKey(s => s.FormId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}