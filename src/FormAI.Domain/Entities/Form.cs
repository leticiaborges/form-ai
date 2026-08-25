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
    public bool IsGraded { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public User? Creator { get; private set; }
    public List<FormQuestion> Questions { get; private set; } = new();
    public List<Submission> Submissions { get; private set; } = new();
    public List<FormSourceContent> SourceContents { get; private set; } = new();

    private Form() { }

    public static Form Create(string title, string description, Guid createdBy, SourceType sourceType,
    bool isPublic, DateTime? expiresAt, bool showResultsAfterSubmit, bool isGraded)
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
            IsGraded = isGraded,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string title, string description, bool isPublic, DateTime? expiresAt,
    bool showResultsAfterSubmit, bool isGraded)
    {
        Title = title;
        Description = description;
        IsPublic = isPublic;
        ExpiresAt = expiresAt;
        ShowResultsAfterSubmit = showResultsAfterSubmit;
        IsGraded = isGraded;
    }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt < DateTime.UtcNow;

    /// <summary>
    /// Makes an ungraded form ungraded: the answer key, the suggested answers and the points are
    /// discarded, which is what makes turning grading off lossy. A graded form is left alone —
    /// its points are the owner's to set.
    /// </summary>
    public void ClearGradingIfUngraded()
    {
        if (IsGraded)
            return;

        foreach (var question in Questions)
            question.ClearAnswerKey();
    }

    public const int DefaultQuestionPoints = 1;

    public void ReplaceQuestions(List<FormQuestion> questions)
    {
        Questions.Clear();
        Questions.AddRange(questions);
    }

    public void AddQuestion(FormQuestion question) => Questions.Add(question);

    public void RemoveQuestion(FormQuestion question) => Questions.Remove(question);
}
