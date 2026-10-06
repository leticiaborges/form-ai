using FormAI.Domain.Enums;

public class GenerateFormDataRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? SourceText { get; init; } = string.Empty;
    public IFormFile? File { get; init; }
    public int QuestionCount { get; init; }
    public QuestionType[]? AllowedTypes { get; init; }
    public DifficultyLevel DifficultyLevel { get; init; }
    public bool IsGraded { get; init; }
    public bool ShowResultsAfterSubmit { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}