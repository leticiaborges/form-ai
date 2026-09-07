using FormAI.Domain.Entities;

namespace FormAI.Domain.Results;

public record FormResults(
    int SubmissionCount,
    int? TotalPoints,
    IReadOnlyList<ScoreBucket> ScoreDistribution,
    IReadOnlyList<QuestionResults> Questions
);

public record ScoreBucket(int Score, int SubmissionCount);

public record QuestionResults(
    FormQuestion Question,
    int AnswerCount,
    int? CorrectAnswerCount,
    IReadOnlyList<OptionResults> Options,
    IReadOnlyList<ValueResults> Values
);

public record OptionResults(string Text, int Count, bool? IsCorrect);

public record ValueResults(string Value, int Count, bool? IsCorrect);