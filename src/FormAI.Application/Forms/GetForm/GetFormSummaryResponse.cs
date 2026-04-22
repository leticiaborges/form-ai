using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GetForm;

public record GetFormSummaryResponse(
    Guid Id,
    string Title,
    bool IsPublic,
    DateTime? ExpiresAt,
    DateTime CreatedAt
);