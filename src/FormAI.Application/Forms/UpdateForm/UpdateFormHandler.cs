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

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.");

        if (request.Title.Length > 255)
            throw new ArgumentException("Title must be at most 255 characters.");

        form.Update(request.Title.Trim(), request.Description ?? string.Empty, request.IsPublic,
        request.ExpiresAt, request.ShowResultsAfterSubmit);

        await _formRepository.UpdateAsync(form, cancellationToken);
    }

}