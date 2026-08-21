using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormResponse(
    Guid FormId,
    string Title,
    SourceType SourceType,
    DateTime CreationAt,
    IReadOnlyList<GeneratedQuestionResponse> Questions
);

public record GeneratedQuestionResponse(
    Guid QuestionId,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    bool AiGenerated,
    decimal? Points,
    string? CorrectAnswer,
    IReadOnlyList<GeneratedOptionResponse> Options
);

public record GeneratedOptionResponse(
    Guid OptionId,
    string Text,
    int Order,
    bool? IsCorrect
);