using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class Form
{
    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; } = string.Empty;
    public Guid CreatedBy { get; private set; }
    public SourceType SourceType { get; private set; }
    public bool IsPublic { get; private set; }
    public DateTime? ExpiresAt { get; private set; }
    public bool ShowResultsAfterSubmit { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public User? Creator { get; private set; }
    public List<FormQuestion> Questions { get; private set; } = new();
    public List<Submission> Submissions { get; private set; } = new();

    private Form() { }

    public static Form Create(string title, string description, Guid createdBy, SourceType sourceType, 
    bool isPublic, DateTime? expiresAt, bool showResultsAfterSubmit)
    {
        return new Form
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            CreatedBy = createdBy,
            SourceType = sourceType,
            IsPublic = isPublic,
            ExpiresAt = expiresAt,
            ShowResultsAfterSubmit = showResultsAfterSubmit,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string title, string description, bool isPublic, DateTime? expiresAt, bool showResultsAfterSubmit)
    {
        Title = title;
        Description = description;
        IsPublic = isPublic;
        ExpiresAt = expiresAt;
        ShowResultsAfterSubmit = showResultsAfterSubmit;
    }

    public void Close() => ExpiresAt = DateTime.UtcNow;

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;

    public void ReplaceQuestions(List<FormQuestion> questions)
    {
        Questions.Clear();
        Questions.AddRange(questions);
    }
}
