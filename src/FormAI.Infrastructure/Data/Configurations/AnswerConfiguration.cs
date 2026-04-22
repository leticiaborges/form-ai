using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class AnswerConfiguration : IEntityTypeConfiguration<Answer>
{
    public void Configure(EntityTypeBuilder<Answer> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TextValue)
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(s => s.NumericValue)
            .IsRequired(false);

        builder.Property(s => s.Score)
            .IsRequired(false);

        builder.HasOne(a => a.Submission)
            .WithMany(s => s.Answers)
            .HasForeignKey(a => a.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Question)
            .WithMany(q => q.Answers)
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.SelectedOptions)
        .WithOne()
        .HasForeignKey(a=> a.AnswerId)
        .OnDelete(DeleteBehavior.Cascade);
    }
}