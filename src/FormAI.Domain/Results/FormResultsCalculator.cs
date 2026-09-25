using System.Globalization;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Scoring;
using Microsoft.VisualBasic;

namespace FormAI.Domain.Results;

public static class FormResultsCalculator
{
    private static readonly StringComparer TextComparer
    = StringComparer.OrdinalIgnoreCase;

    public static FormResults Calculate(Form form,
    IReadOnlyList<Submission> submissions)
    {
        var answersByQuestions = submissions.
        SelectMany(a => a.Answers)
        .GroupBy(a => a.QuestionId)
        .ToDictionary(a => a.Key, a => (IReadOnlyList<Answer>)a.ToList());

        var questions = form.Questions
        .OrderBy(a => a.Order)
        .Select(q => BuildQuestionResults(form,
        q, answersByQuestions.TryGetValue(q.Id,
        out var found) ? found : Array.Empty<Answer>()))
        .ToList();

        return new FormResults(
            submissions.Count,
            SubmissionScorer.MaximumScore(form),
            form.IsGraded ? BuildScoreDistribution(submissions) : Array.Empty<ScoreBucket>(),
            questions);
    }

    private static IReadOnlyList<ScoreBucket> BuildScoreDistribution(IReadOnlyList<Submission> submissions)
    {
        return submissions.Where(a => a.Score.HasValue)
        .GroupBy(a => a.Score!.Value)
        .OrderBy(a => a.Key)
        .Select(a => new ScoreBucket(a.Key, a.Count()))
        .ToList();
    }

    private static QuestionResults BuildQuestionResults(
        Form form, FormQuestion question,
        IReadOnlyList<Answer> answers
    )
    {
        int? correctCount = form.IsGraded
        ? answers.Count(a => SubmissionScorer
        .IsAnswerCorrect(question, ScoredAnswer.From(a))) : null;

        var isSelection = question.Type is Enums.QuestionType.Single
        or Enums.QuestionType.Multiple;

        return new QuestionResults(
            question,
            answers.Count,
            correctCount,
            isSelection ? BuildOptionResults(form, question,
            answers) : Array.Empty<OptionResults>(),
            isSelection ? Array.Empty<ValueResults>() :
            BuildValueResults(form, question, answers)
        );
    }

    private static IReadOnlyList<OptionResults> BuildOptionResults
    (Form form, FormQuestion question, IReadOnlyList<Answer> answers)
    {
        var counts = GroupTexts(answers
        .SelectMany(a => a.SelectedOptions)
        .Select(a => a.OptionText));

        var results = new List<OptionResults>();

        foreach (var option in question.Options.OrderBy(a => a.Order))
        {
            var key = option.Text.Trim();
            counts.Remove(key, out var found);

            results.Add(new OptionResults(option.Text,
            found.Count, form.IsGraded ? option.IsCorrect : null));
        }


        // Whatever is left matched no current option. It can never be marked correct, because
        // there is no option left carrying an answer key.
        results.AddRange(counts.Values
           .OrderByDescending(a => a.Count)
           .ThenBy(a => a.Display, TextComparer)
           .Select(a => new OptionResults(a.Display, a.Count, null)));

        return results;

    }

    private static IReadOnlyList<ValueResults>
    BuildValueResults(Form form, FormQuestion question,
    IReadOnlyList<Answer> answers)
    {
        var texts = question.Type == QuestionType.Numeric ?
            answers.Where(a => a.NumericValue.HasValue)
            .Select(a => a.NumericValue!.Value.ToString(CultureInfo.InvariantCulture))
            : answers.Where(a => !string.IsNullOrWhiteSpace(a.TextValue))
            .Select(a => a.TextValue!);

        return GroupTexts(texts).Values
        .OrderByDescending(a => a.Count)
        .ThenBy(a => a.Display, TextComparer)
        .Select(a => new ValueResults(a.Display, a.Count,
        form.IsGraded ? IsValueCorrect(question, a.Display) : null))
        .ToList();
    }

    private static bool IsValueCorrect(FormQuestion question,
    string value)
    {
        var answer = question.Type == QuestionType.Numeric
        ? new ScoredAnswer(null, double.Parse(value,
        CultureInfo.InvariantCulture), Array.Empty<string>())
        : new ScoredAnswer(value, null, Array.Empty<string>());

        return SubmissionScorer.IsAnswerCorrect(question, answer);
    }

    private static Dictionary<string, (string Display, int Count)> GroupTexts(IEnumerable<string> texts)
    {
        var grouped = new Dictionary<string,
        (string Display, int Count)>(TextComparer);

        foreach (var raw in texts)
        {
            var key = raw.Trim();

            grouped[key] = grouped.TryGetValue(key, out var existing)
            ? (existing.Display, existing.Count + 1)
            : (key, 1);
        }

        return grouped;
    }

}