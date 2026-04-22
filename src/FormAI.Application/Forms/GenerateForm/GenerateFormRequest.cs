using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormRequest(
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    string DifficultyLevel,
    string? Context,
    bool IncludeCorrectAnswers
);
