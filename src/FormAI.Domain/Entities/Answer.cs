using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class Answer
{
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid QuestionId { get; private set; }
    public List<AnswerSelectedOption> SelectedOptions { get; private set; } = new();
    public string? TextValue { get; private set; }
    public double? NumericValue { get; private set; }
    public int? Score { get; private set; }

    public Submission? Submission { get; }
    public FormQuestion? Question { get; }

    private Answer() { }

    public static Answer Create(Guid submissionId, Guid questionId, List<AnswerSelectedOption>? selectedOptions,
    string? textValue, double? numericValue, int? score)
    {
        return new Answer
        {
            Id = Guid.NewGuid(),
            SubmissionId = submissionId,
            QuestionId = questionId,
            SelectedOptions = selectedOptions ?? new(),
            TextValue = textValue,
            NumericValue = numericValue,
            Score = score
        };
    }

    public void SetSelectedOptions(List<AnswerSelectedOption> options)
    {
        SelectedOptions.Clear();
        SelectedOptions.AddRange(options);
    }

    public void SetScore(int? score) => Score = score;
}
