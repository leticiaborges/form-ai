using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.UpdateForm;

public class UpdateFormHandler
{
    public readonly IFormRepository _formRepository;

    public UpdateFormHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    public async Task HandleAsync(UpdateFormRequest request, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException("Form not found");

        if (form.CreatedBy != request.RequestingUserId)
             throw new ForbiddenException("You do not own this form.");

        form.Update(request.Title, request.Description ?? string.Empty, request.IsPublic,
        request.ExpiresAt, request.ShowResultsAfterSubmit);

        await _formRepository.UpdateAsync(form, cancellationToken);
    }

}