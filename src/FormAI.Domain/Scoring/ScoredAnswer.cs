namespace FormAI.Domain.Scoring;

public record ScoredAnswer(
    string? TextValue,
    double? NumericValue,
    IReadOnlyCollection<string> SelectedOptionTexts
)
{
    public static readonly ScoredAnswer Unanswered =
        new(null, null, Array.Empty<string>());

    public static ScoredAnswer From(Entities.Answer answer) =>
    new(answer.TextValue, answer.NumericValue,
        answer.SelectedOptions.Select(o => o.OptionText).ToList());
}
