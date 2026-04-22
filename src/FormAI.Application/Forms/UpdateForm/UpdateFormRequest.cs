using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.UpdateForm;

public record UpdateFormRequest(
    Guid FormId,
    string Title,
    Guid RequestingUserId,
    string? Description,
    bool IsPublic,
    DateTime? ExpiresAt,
    bool ShowResultsAfterSubmit
);
