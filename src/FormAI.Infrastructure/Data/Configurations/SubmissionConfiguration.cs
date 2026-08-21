using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.RespondentToken)
            .IsRequired();

        builder.Property(s => s.IpAddress)
            .HasMaxLength(45);

        builder.Property(s => s.SubmittedAt)
            .IsRequired();

        builder.Property(s => s.Score)
            .IsRequired(false);

        builder.HasIndex(s => new { s.FormId, s.RespondentToken });

        builder.HasIndex(s => new { s.FormId, s.UserId })
            .IsUnique()
            .HasFilter("\"user_id\" IS NOT NULL");

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}