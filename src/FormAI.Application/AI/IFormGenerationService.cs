using FormAI.Domain.Enums;

namespace FormAI.Application.AI;

public record GenerationParameters(
    int QuestionCount = 10,
    QuestionType[]? AllowedTypes = null,
    string DifficultyLevel = "medium",
    bool IncludeCorrectAnswers = false
);

public record GeneratedQuestion(
    string Text,
    QuestionType Type,
    bool IsRequired,
    int? Points,
    string? CorrectAnswer,
    IReadOnlyList<GeneratedOption> Options
);

public record GeneratedOption(string Text, bool IsCorrect);

public interface IFormGenerationService
{
    Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        CancellationToken cancellationToken = default);
}
