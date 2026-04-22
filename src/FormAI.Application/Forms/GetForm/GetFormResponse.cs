using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GetForm;

public record GetFormResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsPublic,
    DateTime? ExpiresAt,
    bool ShowResultsAfterSubmit,
    DateTime CreatedAt,
    List<QuestionDTO> Questions
);

public record QuestionDTO(
    Guid Id,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    int? Points,
    List<OptionDTO> Options
);

public record OptionDTO(
    Guid Id,
    string? Text,
    int Order
);
