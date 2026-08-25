
namespace FormAI.Application.Forms.GetSubmissionCount;

public record GetSubmissionCountRequest(Guid FormId, Guid RequestingUserId);

public record GetSubmissionCountResponse(int SubmissionCount);