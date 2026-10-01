using FormAI.Domain.Enums;

namespace FormAI.Application.AI;

public record GenerationParameters(
    int QuestionCount = 10,
    QuestionType[]? AllowedTypes = null,
    DifficultyLevel DifficultyLevel = DifficultyLevel.Medium,
    bool IncludeCorrectAnswers = false
);

public record GeneratedQuestion(
    string Text,
    QuestionType Type,
    bool IsRequired,
    string? CorrectAnswer,
    IReadOnlyList<GeneratedOption> Options
);

public record GeneratedOption(string Text, bool? IsCorrect);

public interface IFormGenerationService
{
    Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        Guid userId,
        CancellationToken cancellationToken = default);
}
