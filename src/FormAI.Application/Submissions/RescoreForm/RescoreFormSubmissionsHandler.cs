using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Scoring;

namespace FormAI.Application.Submissions.RescoreForm;

/// <summary>
/// Recomputes the score of every submission of a form against the form as it now stands.
///
/// Called by the editor save whenever a change affects grading, and the seam the future
/// "owner edits a question's points" flow reuses. It mutates the tracked submissions and leaves
/// committing to the caller, so the rescore and the save that caused it are one transaction.
/// </summary>
public class RescoreFormSubmissionsHandler
{
    private readonly ISubmissionRepository _submissions;

    public RescoreFormSubmissionsHandler(ISubmissionRepository submissions)
    {
        _submissions = submissions;
    }

    public async Task HandleAsync(Form form, CancellationToken cancellationToken = default)
    {
        var submissions = await _submissions.GetByFormForScoringAsync(form.Id, cancellationToken);
        var questionsById = form.Questions.ToDictionary(q => q.Id);

        foreach (var submission in submissions)
            Rescore(form, questionsById, submission);
    }

    private static void Rescore(Form form,
        IReadOnlyDictionary<Guid, FormQuestion> questionsById, Submission submission)
    {
        var scoredAnswers = new Dictionary<Guid, ScoredAnswer>();

        foreach (var answer in submission.Answers)
        {
            var scoredAnswer = ScoredAnswer.From(answer);

            // An answer to a question the owner has since deleted is kept as a record of what the
            // respondent said, but there is nothing left to grade it against.
            if (!questionsById.TryGetValue(answer.QuestionId, out var question))
            {
                answer.SetScore(null);
                continue;
            }

            scoredAnswers[answer.QuestionId] = scoredAnswer;
            answer.SetScore(SubmissionScorer.ScoreAnswer(form, question, scoredAnswer));
        }

        submission.SetScore(SubmissionScorer.ScoreSubmission(form, scoredAnswers));
    }
}
