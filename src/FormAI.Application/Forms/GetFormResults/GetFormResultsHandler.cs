using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Domain.Results;

namespace FormAI.Application.Forms.GetFormResults;

public class GetFormResultsHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public GetFormResultsHandler(IFormRepository forms,
        ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<GetFormResultsResponse> HandleAsync
    (GetFormResultsRequest request,
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

        var submissions = await _submissions.GetByFormWithAnswersAsync
        (request.FormId, cancellationToken);

        var results = FormResultsCalculator.Calculate(form, submissions);

        return new GetFormResultsResponse(
            form.Id,
            form.Title,
            form.IsGraded,
            results.SubmissionCount,
            results.TotalPoints,
            results.ScoreDistribution.Select(a => new ScoreBucketResponse(a.Score, a.SubmissionCount)).ToList(),
            results.Questions.Select(q =>
                new QuestionResultResponse(
                    q.Question.Id,
                    q.Question.Text,
                    q.Question.Type,
                    q.Question.Order,
                    q.AnswerCount,
                    q.Question.Points,
                    q.CorrectAnswerCount,
                    q.Options.Select(o => new OptionResultResponse(o.Text,
                    o.Count, o.IsCorrect)).ToList(),
                    q.Values.Select(v => new ValueResultResponse(v.Value,
                    v.Count, v.IsCorrect)).ToList()

                )).ToList()
        );
    }

}