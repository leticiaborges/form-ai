using FormAI.Application.Common.Exceptions;
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

    public async Task<GetFormResponse> HandleAsync(Guid formId,
        Guid? requestingUserId, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(formId, cancellationToken);
        if (form == null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic && form.CreatedBy != requestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        var formResponse = new GetFormResponse(form.Id, form.Title, form.Description,
        form.IsPublic, form.ExpiresAt, form.ShowResultsAfterSubmit, form.CreatedAt,
        BuildQuestionDTOList(form));
        
        return formResponse;
    }

    private List<QuestionDTO> BuildQuestionDTOList(Form form)
    {
        return form.Questions.Select(a => 
            new QuestionDTO(a.Id,a.Text, a.Type, a.Order, a.IsRequired, a.Points, 
            a.Options.Select(o => new OptionDTO(o.Id, o.Text, o.Order)).OrderBy(o => o.Order).ToList()))
            .OrderBy(a => a.Order)
            .ToList();
    }
}