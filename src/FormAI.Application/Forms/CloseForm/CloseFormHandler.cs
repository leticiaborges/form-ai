
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.CloseForm;

public class CloseFormHandler
{
    private readonly IFormRepository _forms;

    public CloseFormHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        CloseFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException($"Form not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        form.Close();

        await _forms.UpdateAsync(form, cancellationToken);
    }
}