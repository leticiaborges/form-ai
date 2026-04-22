using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class FormQuestionConfiguration : IEntityTypeConfiguration<FormQuestion>
{
    public void Configure(EntityTypeBuilder<FormQuestion> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Text)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(u => u.Type)
            .IsRequired();

        builder.Property(u => u.Order)
            .IsRequired();

        builder.Property(u => u.IsRequired)
            .IsRequired();

        builder.Property(u => u.AiGenerated)
            .IsRequired();

        builder.Property(u => u.Points)
            .IsRequired(false);

        builder.Property(u => u.CorrectAnswer)
            .HasMaxLength(1024);
    }
}