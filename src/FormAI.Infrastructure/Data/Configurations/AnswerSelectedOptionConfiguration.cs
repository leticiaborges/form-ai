using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class AnswerSelectedOptionConfiguration : IEntityTypeConfiguration<AnswerSelectedOption>
{
    public void Configure(EntityTypeBuilder<AnswerSelectedOption> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.OptionText)
            .HasMaxLength(1024)
            .IsRequired();
    }
}