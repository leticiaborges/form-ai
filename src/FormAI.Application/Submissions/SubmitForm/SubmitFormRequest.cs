namespace FormAI.Application.Submissions.SubmitForm;

public record SubmitFormRequest(
    Guid FormId,
    Guid RespondentToken,
    IReadOnlyList<AnswerRequest> Answers
);

public record AnswerRequest(
    Guid QuestionId,
    Guid[]? SelectedOptionIds,
    string? TextValue,
    decimal? NumericValue
);

public record SubmitFormResponse(Guid SubmissionId, decimal? Score);
