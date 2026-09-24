using System.Globalization;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Domain.Scoring;

/// <summary>
/// Computes the score of an answer and of a submission from the form <em>as it stands</em>.
///
/// A score is derived, never a snapshot: the same rules run when a form is submitted and again
/// whenever an editor save changes something that affects grading. See ADR 0004.
/// </summary>
public static class SubmissionScorer
{
    /// <summary>
    /// Option text is unique within a question when trimmed and compared case-insensitively
    /// (<c>QuestionOptionValidator</c>), so matching a selected option uses the same comparison.
    /// </summary>
    private static readonly StringComparer OptionTextComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// The score of one answer: null when the form is not graded, otherwise the question's points
    /// when the answer is right and 0 when it is wrong, unanswered, or has no answer key to be
    /// measured against.
    /// </summary>
    public static int? ScoreAnswer(Form form, FormQuestion question, ScoredAnswer answer)
    {
        if (!form.IsGraded)
            return null;

        return IsAnswerCorrect(question, answer) ? question.Points ?? 0 : 0;
    }

    /// <summary>
    /// The score of a whole submission: null when the form is not graded, otherwise the sum of
    /// its answers. Questions the respondent never answered count as 0, so a question added to
    /// the form after a submission was made cannot change that submission's total.
    /// </summary>
    public static int? ScoreSubmission(Form form, IReadOnlyDictionary<Guid, ScoredAnswer> answersByQuestionId)
    {
        if (!form.IsGraded)
            return null;

        return form.Questions.Sum(question =>
        {
            var answer = answersByQuestionId.TryGetValue(question.Id, out var found)
                ? found
                : ScoredAnswer.Unanswered;

            return ScoreAnswer(form, question, answer) ?? 0;
        });
    }

    /// <summary>
    /// The most a submission to this form can score: null when the form is not graded, otherwise
    /// the sum of its questions' points, a question with no points counting 0. Like every score,
    /// it is read from the form as it stands, never stored.
    /// </summary>
    public static int? MaximumScore(Form form)
    {
        if (!form.IsGraded)
            return null;

        return form.Questions.Sum(question => question.Points ?? 0);
    }

    public static bool IsAnswerCorrect(FormQuestion question, ScoredAnswer answer) => question.Type switch
    {
        QuestionType.Single or QuestionType.Multiple => IsSelectionCorrect(question, answer),
        QuestionType.Text => IsTextCorrect(question, answer),
        QuestionType.Numeric => IsNumericCorrect(question, answer),
        _ => false
    };

    private static bool IsSelectionCorrect(FormQuestion question, ScoredAnswer answer)
    {
        var correctTexts = question.Options
            .Where(o => o.IsCorrect == true)
            .Select(o => o.Text.Trim())
            .ToHashSet(OptionTextComparer);

        // No answer key: the question cannot be earned.
        if (correctTexts.Count == 0)
            return false;

        var selectedTexts = answer.SelectedOptionTexts
            .Select(t => t.Trim())
            .ToHashSet(OptionTextComparer);

        // All or nothing: picking three of four correct options earns nothing.
        return selectedTexts.SetEquals(correctTexts);
    }

    private static bool IsTextCorrect(FormQuestion question, ScoredAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
            return false;

        if (string.IsNullOrWhiteSpace(answer.TextValue))
            return false;

        return string.Equals(answer.TextValue.Trim(), question.CorrectAnswer.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericCorrect(FormQuestion question, ScoredAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || answer.NumericValue is null)
            return false;

        var parsed = double.TryParse(question.CorrectAnswer.Trim(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var expected);

        return parsed && Math.Round(answer.NumericValue.Value, 5) == Math.Round(expected, 5);
    }
}
