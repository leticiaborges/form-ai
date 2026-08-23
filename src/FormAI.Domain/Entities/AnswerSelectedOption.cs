namespace FormAI.Domain.Entities;

public class AnswerSelectedOption
{
    public Guid Id { get; private set; }
    public Guid AnswerId { get; private set; }
    public string OptionText { get; private set; } = string.Empty;

    private AnswerSelectedOption() { }

    public static AnswerSelectedOption Create(Guid answerId, string optionText)
    {
        if (string.IsNullOrWhiteSpace(optionText))
            throw new ArgumentException("Option text is required.", nameof(optionText));

        return new AnswerSelectedOption
        {
            Id = Guid.NewGuid(),
            AnswerId = answerId,
            OptionText = optionText.Trim()
        };
    }
}
