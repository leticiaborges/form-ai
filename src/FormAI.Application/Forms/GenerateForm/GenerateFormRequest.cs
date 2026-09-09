using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormRequest(
    string? Title,
    string? Description,
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    string DifficultyLevel,
    bool IsGraded,
    DateTime ExpiresAt
);