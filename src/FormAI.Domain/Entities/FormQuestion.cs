using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class FormQuestion
{
    public Guid Id { get; private set; }
    public Guid FormId { get; private set; }
    public string Text { get; private set; }
    public QuestionType Type { get; private set; }
    public int Order { get; private set; }
    public bool IsRequired { get; private set; }
    public bool AiGenerated { get; private set; }
    public int? Points { get; private set; }
    public string? CorrectAnswer { get; private set; }
    public Form? Form { get; private set; }
    public List<QuestionOption> Options { get; private set; } = new();
    public List<Answer> Answers { get; private set; } = new();

    private FormQuestion() { }

    public static FormQuestion Create(Guid formId, string text, QuestionType type, int order, bool isRequired,
    bool aiGenerated, int? points, string? correctAnswer)
    {
        return new FormQuestion
        {
            Id = Guid.NewGuid(),
            Text = text,
            FormId = formId,
            Type = type,
            Order = order,
            IsRequired = isRequired,
            AiGenerated = aiGenerated,
            Points = points,
            CorrectAnswer = correctAnswer
        };
    }

    public void SetOptions(List<QuestionOption> options)
    {
        Options.Clear();
        Options.AddRange(options);
    }

    public void Update(string text, QuestionType type, int order, bool isRequired,
    bool aiGenerated, int? points, string? correctAnswer)
    {
        Text = text;
        Type = type;
        Order = order;
        IsRequired = isRequired;
        AiGenerated = aiGenerated;
        Points = points;
        CorrectAnswer = correctAnswer;
    }

    public void AddOption(QuestionOption option) => Options.Add(option);

    public void RemoveOption(QuestionOption option) => Options.Remove(option);

    /// <summary>
    /// Discards everything that only exists on a graded form: the suggested answer, the points
    /// and the answer key on every option. Options themselves are kept — only their marking goes.
    /// </summary>
    public void ClearAnswerKey()
    {
        Points = null;
        CorrectAnswer = null;

        foreach (var option in Options)
            option.ClearAnswerKey();
    }
}
