using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using System.Linq;

namespace FormAI.Application.Forms.GetForm;

public class GetFormHandler
{
    private readonly IFormRepository _formRepository;

    public GetFormHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    /// <summary>
    /// The form as its owner sees it in the editor, answer key and suggested answers included.
    /// Owner-only for that reason: this is the one response that carries the answers, and a
    /// respondent must not be able to read it. Answering uses <c>GetFormToAnswerHandler</c>,
    /// which leaves the key out.
    /// </summary>
    public async Task<GetFormResponse> HandleAsync(Guid formId,
        Guid requestingUserId, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(formId, cancellationToken);

        FormAccessValidator.CheckOwnerAccess(form, requestingUserId);

        var formResponse = new GetFormResponse(form!.Id, form.Title, form.Description,
        form.IsPublic, form.ExpiresAt, form.ShowResultsAfterSubmit, form.IsGraded, form.CreatedAt,
        BuildQuestionDTOList(form));

        return formResponse;
    }

    private List<QuestionDTO> BuildQuestionDTOList(Form form)
    {
        return form.Questions.Select(a =>
            new QuestionDTO(a.Id, a.Text, a.Type, a.Order, a.IsRequired, a.AiGenerated, a.Points, a.CorrectAnswer,
            a.Options.Select(o => new OptionDTO(o.Id, o.Text, o.Order, o.IsCorrect)).OrderBy(o => o.Order).ToList()))
            .OrderBy(a => a.Order)
            .ToList();
    }
}