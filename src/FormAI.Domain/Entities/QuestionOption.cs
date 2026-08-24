using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class QuestionOption
{
    public Guid Id { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Text { get; private set; }
    public int Order { get; private set; }
    public bool? IsCorrect { get; private set; }
    public FormQuestion? Question { get; private set; }

    private QuestionOption() { }

    public static QuestionOption Create(Guid questionId, string text, int order, bool? isCorrect)
    {
        return new QuestionOption
        {
            Id = Guid.NewGuid(),
            QuestionId = questionId,
            Text = text,
            Order = order,
            IsCorrect = isCorrect
        };
    }

    public void Update(string text, int order, bool? isCorrect)
    {
        Text = text;
        Order = order;
        IsCorrect = isCorrect;
    }
}
