using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Forms;

public class FormAccessValidatorTests
{
    [Theory]
    [InlineData(true)]  // the owner gets no carve-out on the answering path
    [InlineData(false)] // a stranger, or an anonymous respondent
    public void CheckUserAnswerAccess_PrivateForm_ThrowsNotFound(bool requesterIsOwner)
    {
        var form = NewForm(isGraded: false, isPublic: false);
        Guid? requester = requesterIsOwner ? form.CreatedBy : null;

        Assert.Throws<NotFoundException>(() =>
            FormAccessValidator.CheckUserAnswerAccess(form, requester));
    }

    [Fact]
    public void CheckUserAnswerAccess_ExpiredForm_ThrowsFormExpired()
    {
        var form = NewForm(isGraded: false, expiresAt: DateTime.UtcNow.AddDays(-1));

        var exception = Assert.Throws<ValidationException>(() =>
            FormAccessValidator.CheckUserAnswerAccess(form, form.CreatedBy));

        Assert.Equal(ValidationErrorCode.FormExpired, exception.Code);
    }

    [Fact]
    public void CheckUserAnswerAccess_PublishedAndNotExpired_DoesNotThrow()
    {
        var form = NewForm(isGraded: false, expiresAt: DateTime.UtcNow.AddDays(1));

        var exception = Record.Exception(() =>
            FormAccessValidator.CheckUserAnswerAccess(form, null));

        Assert.Null(exception);
    }
}
