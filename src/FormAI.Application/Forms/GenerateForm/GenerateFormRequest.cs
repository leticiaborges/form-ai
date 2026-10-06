using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormRequest(
    string? Title,
    string? Description,
    string SourceText,
    SourceType SourceType,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    DifficultyLevel DifficultyLevel,
    bool IsGraded,
    bool ShowResultsAfterSubmit,
    DateTime ExpiresAt,
    SourceFile? File = null
);