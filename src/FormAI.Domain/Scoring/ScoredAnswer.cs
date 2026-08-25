namespace FormAI.Domain.Scoring;

public record ScoredAnswer(
    string? TextValue,
    double? NumericValue,
    IReadOnlyCollection<string> SelectedOptionTexts
)
{
    public static readonly ScoredAnswer Unanswered =
        new(null, null, Array.Empty<string>());
}
