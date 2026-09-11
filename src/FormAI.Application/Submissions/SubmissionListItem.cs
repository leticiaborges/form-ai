namespace FormAI.Application.Submissions;

public record class SubmissionListItem(
    Guid SubmissionId,
    DateTime SubmittedAt
);