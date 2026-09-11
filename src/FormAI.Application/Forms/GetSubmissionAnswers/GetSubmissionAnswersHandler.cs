using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Scoring;

namespace FormAI.Application.Forms.GetSubmissionAnswers;

public class GetSubmissionAnswersHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public GetSubmissionAnswersHandler(IFormRepository forms,
        ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<GetSubmissionAnswersResponse> HandleAsync
    (GetSubmissionAnswersRequest request,
    CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId,
        cancellationToken);

        if (form is null)
            throw new NotFoundException("Form not found");

        if (!form.IsPublic && form.CreatedBy != request.RequestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        var submission = await _submissions.GetByIdWithAnswersNoTrackingAsync
        (request.SubmissionId, request.FormId, cancellationToken);

        if (submission == null)
            throw new NotFoundException("Submission not found.");

        var dictionaryQuestions = form.Questions.GroupBy(a => a.Id)
                                  .ToDictionary(a => a.Key, a => a.First());

        return new GetSubmissionAnswersResponse(
            submission.FormId,
            submission.Id,
            submission.SubmittedAt,
            submission.Score,
            Answers: BuildListAnswerReponse(submission.Answers, form, dictionaryQuestions)
        );
    }

    private bool IsCorrect(Answer answer, FormQuestion question)
    {
        var scoredAnswer = ScoredAnswer.From(answer);
        var result = SubmissionScorer.IsAnswerCorrect(question, scoredAnswer);
        return result;
    }

    private IEnumerable<AnswerResponse> BuildListAnswerReponse(IEnumerable<Answer> answers, Form form,
        Dictionary<Guid, FormQuestion> dictionaryQuestions)
    {
        var list = new List<AnswerResponse>();

        foreach (var answer in answers)
        {
            dictionaryQuestions.TryGetValue(answer.QuestionId, out var question);
            if (question != null)
            {
                list.Add(new AnswerResponse(question.Id,
                 answer.SelectedOptions.Select(b => b.OptionText).ToArray(),
                 answer.NumericValue,
                 answer.TextValue,
                 form.IsGraded ? IsCorrect(answer, question) : null,
                 answer.Score));
            }
        }

        return list;
    }

}