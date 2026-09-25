
using System.Linq.Expressions;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.UnitTests.TestsHelper;

public static class EntityBuilders
{
    public static Form NewForm(bool isGraded, DateTime? expiresAt = null, bool isPublic = true,
        bool showResultsAfterSubmit = false) =>
            Form.Create("Quiz", "",
            Guid.NewGuid(), SourceType.Text, isPublic, expiresAt,
             showResultsAfterSubmit, isGraded);

    public static FormQuestion AddQuestion(Form form, QuestionType type,
        int order, bool isRequired = false,
        int? points = null, string? correctAnswer = null,
        params (string Text, bool? IsCorrect)[] options)
    {
        var question = FormQuestion.Create(form.Id, $"Q{order}",
        type, order, isRequired, false, points, correctAnswer);

        question.SetOptions(options
            .Select((o, i) => QuestionOption.Create(question.Id, o.Text, i + 1, o.IsCorrect))
            .ToList());

        form.AddQuestion(question);
        return question;
    }
}