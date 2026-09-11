namespace FormAI.Application.Forms.GetSubmissionAnswers;

public record GetSubmissionAnswersResponse(
    Guid FormId,
    Guid SubmissionId,
    DateTime SubmittedAt,
    int? Score,
    IEnumerable<AnswerResponse> Answers
);

public record AnswerResponse(
    Guid QuestionId,
    string[] SelectedOptions,
    double? NumericValue,
    string? TextValue,
    bool? IsCorrect,
    int? Score
);
