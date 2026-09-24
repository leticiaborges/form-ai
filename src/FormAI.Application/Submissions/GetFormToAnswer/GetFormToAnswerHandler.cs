using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;

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
        FormAccessValidator.CheckUserAnswerAccess(form, request.RequestingUserId);

        var questions = form!.Questions.OrderBy(q => q.Order).Select(q =>
            new AnswerQuestionDTO(q.Id, q.Text, q.Type, q.Order, q.IsRequired,
                q.Options.OrderBy(o => o.Order)
                    .Select(o => new AnswerOptionDTO(o.Id, o.Text, o.Order))
                    .ToList()))
            .ToList();

        return new GetFormToAnswerResponse(form.Id, form.Title, form.Description, questions);
    }
}