using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.DeleteForm;

public class DeleteFormHandler
{
    public readonly IFormRepository _formRepository;

    public DeleteFormHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    public async Task<bool> HandleAsync(DeleteFormRequest request, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException("Form not found");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form");

        return await _formRepository.DeleteAsync(request.FormId, cancellationToken);
    }
}