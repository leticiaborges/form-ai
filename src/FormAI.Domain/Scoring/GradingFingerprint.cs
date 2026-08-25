using System.Text;
using FormAI.Domain.Entities;

namespace FormAI.Domain.Scoring;

/// <summary>
/// A fingerprint of everything about a form that can change a stored submission's score. Taken
/// before and after an editor save; when the two differ, the form's submissions are rescored.
///
/// It deliberately covers only the questions that already existed, because a question added by
/// the save was not answered by anyone: an unanswered question scores 0, so an addition cannot
/// change any stored total. A question that disappears from the fingerprint is a deletion, and
/// that does change totals.
///
/// Option <em>text</em> is part of the fingerprint because scoring matches on text (ADR 0001),
/// so renaming an option changes who is counted as having answered correctly.
/// </summary>
public static class GradingFingerprint
{
    public static string Of(Form form, IEnumerable<Guid> questionIds)
    {
        var wanted = questionIds.ToHashSet();
        var builder = new StringBuilder();

        builder.Append(form.IsGraded).Append('|');

        foreach (var question in form.Questions.Where(q => wanted.Contains(q.Id)).OrderBy(q => q.Id))
        {
            builder
                .Append(question.Id).Append(':')
                .Append(question.Type).Append(':')
                .Append(question.Points).Append(':')
                .Append(question.CorrectAnswer).Append(':');

            foreach (var option in question.Options.OrderBy(o => o.Order))
                builder.Append(option.Text).Append('=').Append(option.IsCorrect).Append(',');

            builder.Append('|');
        }

        return builder.ToString();
    }
}
