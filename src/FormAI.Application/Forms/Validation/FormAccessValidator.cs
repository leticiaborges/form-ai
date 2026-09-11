using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.Validation;

public static class FormAccessValidator
{
    public static void CheckOwnerAccess(Form? form, Guid requestingUserId)
    {
        if (form is null)
            throw new NotFoundException("Form not found");

        if (form.CreatedBy != requestingUserId)
            throw new NotFoundException("Form not found");
    }

    public static void CheckUserAnswerAccess(Form? form, Guid? requestingUserId)
    {
        if (form is null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic)
            throw new NotFoundException("Form not found.");

        if (form.IsExpired)
            throw new ValidationException(ValidationErrorCode.FormExpired,
                "This form is no longer accepting submissions.");
    }

}
