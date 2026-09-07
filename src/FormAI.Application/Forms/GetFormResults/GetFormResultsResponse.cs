using FormAI.Domain.Enums;
using FormAI.Domain.Results;

namespace FormAI.Application.Forms.GetFormResults;

public record GetFormResultsResponse(
    Guid FormId,
    string Title,
    bool IsGraded,
    int SubmissionCount,
    int? TotalPoints,
    List<ScoreBucketResponse> ScoreDistribution,
    List<QuestionResultResponse> Questions
);

public record ScoreBucketResponse(int Score,
int SubmissionCount);

public record QuestionResultResponse(
    Guid QuestionId,
    string Text,
    QuestionType Type,
    int Order,
    int AnswerCount,
    int? Points,
    int? CorrectAnswerCount,
    List<OptionResultResponse> Options,
    List<ValueResultResponse> Values
);

public record OptionResultResponse(string Text,
int Count, bool? IsCorrect);

public record ValueResultResponse(string Value,
int Count, bool? IsCorrect);
