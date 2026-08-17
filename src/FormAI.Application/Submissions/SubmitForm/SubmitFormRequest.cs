namespace FormAI.Application.Submissions.SubmitForm;

public record SubmitFormRequest(
    Guid FormId,
    Guid RespondentToken,
    IReadOnlyList<AnswerRequest> Answers
);

public record SubmitFormRequestCommand(
    Guid FormId,
    Guid? UserId,
    string IpAddress,
    Guid RespondentToken,
    IReadOnlyList<AnswerRequest> Answers
);

public record AnswerRequest(
    Guid QuestionId,
    Guid[]? SelectedOptionIds,
    string? TextValue,
    decimal? NumericValue
);