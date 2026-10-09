using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.DeleteForm;

public class DeleteFormHandler
{
    private readonly IFormRepository _formRepository;

    public DeleteFormHandler(IFormRepository formRepository)
    {
        _formRepository = formRepository;
    }

    public async Task<bool> HandleAsync(DeleteFormRequest request, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(request.FormId, cancellationToken);

        FormAccessValidator.CheckOwnerAccess(form, request.RequestingUserId);

        return await _formRepository.DeleteAsync(request.FormId, cancellationToken);
    }
}