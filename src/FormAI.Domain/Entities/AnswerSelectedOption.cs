namespace FormAI.Domain.Entities;

public class AnswerSelectedOption
{
    public Guid AnswerId { get; private set; }
    public Guid OptionId { get; private set; }

    public QuestionOption Option { get; private set; }

    private AnswerSelectedOption() { }

    public static AnswerSelectedOption Create(Guid answerId, Guid optionId)
    {
        return new AnswerSelectedOption
        {
            AnswerId = answerId,
            OptionId = optionId
        };
    }
}
