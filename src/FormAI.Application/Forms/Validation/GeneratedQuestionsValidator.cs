using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;

namespace FormAI.Application.Forms.Validation;

public static class GeneratedQuestionsValidator
{
    public const int MaxTextLength = 1024;

    public static void Validate(IReadOnlyList<GeneratedQuestion> questions)
    {
        var problems = new List<string>();
        if (questions.Count == 0)
            problems.Add("There are no questions.");

        for (var i = 0; i < questions.Count; i++)
        {
            ValidateQuestion(questions, problems, i);
        }

        if (problems.Count > 0)
            throw new GenerationException(
                ValidationErrorCode.GenerationOutputInvalid,
                "The AI returned a form we could not use. Please try again.",
                new InvalidOperationException(string.Join(" ", problems)));
    }

    private static void ValidateQuestion(IReadOnlyList<GeneratedQuestion> questions, List<string> problems, int i)
    {
        var q = questions[i];
        var where = $"Question {i + 1}";

        if (string.IsNullOrWhiteSpace(q.Text))
            problems.Add($"{where}: text is required.");
        else if (q.Text.Length > MaxTextLength)
            problems.Add($"{where}: text is over {MaxTextLength} characters.");

        if (q.CorrectAnswer?.Length > MaxTextLength)
            problems.Add($"{where}: correctAnswer is over {MaxTextLength} characters.");

        if (!Enum.IsDefined(q.Type))
        {
            problems.Add($"{where}: type is not valid.");
            return;
        }

        var needsOptions = q.Type is Domain.Enums.QuestionType.Single or Domain.Enums.QuestionType.Multiple;

        if (needsOptions && q.Options.Count < 2)
            problems.Add($"{where}: needs at least 2 options.");
        if (!needsOptions && q.Options.Count != 0)
            problems.Add($"{where}: must have no options.");

        var optionErrors = new Dictionary<string, string[]>();
        QuestionOptionValidator.Validate(i, q.Options.Select(o => o.Text).ToList(), optionErrors);
        problems.AddRange(optionErrors.Values.SelectMany(m => m).Select(m => $"{where}: {m}"));
    }
}
