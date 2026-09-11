namespace FormAI.Application.Forms.GetSubmissionAnswers;

public record GetSubmissionAnswersRequest(
    Guid FormId,
    Guid SubmissionId,
    Guid RequestingUserId
);

