using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.CreateForm;

public record CreateFormRequest(
    string Title,
    string? Description,
    bool IsPublic,
    DateTime? ExpiresAt,
    bool ShowResultsAfterSubmit
);

public record CreateFormResponse(Guid Id, string Title);
