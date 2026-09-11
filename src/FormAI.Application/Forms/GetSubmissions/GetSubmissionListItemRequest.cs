namespace FormAI.Application.Forms.GetSubmissions;

public record GetSubmissionListItemRequest(
    Guid FormId, Guid RequestingUserId, int Page, int PageSize
);

