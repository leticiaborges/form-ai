
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.GetFormToAnswer;

public class GetFormToAnswerHandler
{
    private readonly IFormRepository _forms;

    public GetFormToAnswerHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task<GetFormToAnswerResponse> HandleAsync(GetFormToAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form is null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic && form.CreatedBy != request.RequestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        var questions = form.Questions.OrderBy(q => q.Order).Select(q =>
            new AnswerQuestionDTO(q.Id, q.Text, q.Type, q.Order, q.IsRequired,
                q.Options.OrderBy(o => o.Order)
                    .Select(o => new AnswerOptionDTO(o.Id, o.Text, o.Order))
                    .ToList()))
            .ToList();

        return new GetFormToAnswerResponse(form.Id, form.Title, form.Description, form.IsExpired, questions);
    }
}