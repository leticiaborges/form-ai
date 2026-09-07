
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.UnitTests.Results;

public static class FormResultsCalculatorTestsHelper
{
    public static Form NewForm(bool isGraded) =>
            Form.Create("Quiz", "", Guid.NewGuid(), SourceType.Text, true, null, false, isGraded);

    public static FormQuestion AddQuestion(Form form, QuestionType type, int order,
        int? points = null, string? correctAnswer = null, params (string Text, bool? IsCorrect)[] options)
    {
        var question = FormQuestion.Create(form.Id, $"Q{order}", type, order, false, false, points, correctAnswer);

        question.SetOptions(options
            .Select((o, i) => QuestionOption.Create(question.Id, o.Text, i + 1, o.IsCorrect))
            .ToList());

        form.AddQuestion(question);
        return question;
    }

    public static Submission NewSubmission(Form form, int? score, params Answer[] answers)
    {
        var submission = Submission.Create(form.Id, null, Guid.NewGuid(), "127.0.0.1", score);
        submission.SetAnswers(answers.ToList());
        return submission;
    }

    public static Answer Picked(Guid questionId, params string[] optionTexts)
    {
        var answer = Answer.Create(Guid.NewGuid(), questionId, null, null, null, null);

        answer.SetSelectedOptions(optionTexts
            .Select(t => AnswerSelectedOption.Create(answer.Id, t))
            .ToList());

        return answer;
    }

    public static Answer Wrote(Guid questionId, string text) =>
        Answer.Create(Guid.NewGuid(), questionId, null, text, null, null);

    public static Answer Entered(Guid questionId, double value) =>
        Answer.Create(Guid.NewGuid(), questionId, null, null, value, null);

}