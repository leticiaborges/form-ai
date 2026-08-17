
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.GetMySubmission;

public record GetMySubmissionRequest(Guid FormId, Guid? RequestingUserId, Guid RespondentToken);

public record GetMySubmissionResponse(bool HasSubmitted, DateTime? SubmittedAt);