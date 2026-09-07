namespace FormAI.Application.Forms.GetFormResults;

public record GetFormResultsRequest(
    Guid FormId, Guid RequestingUserId
);

