using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class AnswerSelectedOptionConfiguration : IEntityTypeConfiguration<AnswerSelectedOption>
{
    public void Configure(EntityTypeBuilder<AnswerSelectedOption> builder)
    {
        builder.HasKey(a => new { a.AnswerId, a.OptionId });

        builder.HasOne(a => a.Option)
            .WithMany()
            .HasForeignKey(a => a.OptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}