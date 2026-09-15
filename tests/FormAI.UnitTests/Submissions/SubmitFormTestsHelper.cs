using FormAI.Application.Submissions.SubmitForm;
using FormAI.Domain.Entities;

namespace FormAI.UnitTests.Submissions;

public static class SubmitFormTestsHelper
{
    public static SubmitFormRequestCommand Request(Form form, Guid respondentToken,
        params AnswerRequest[] answers) =>
        new(form.Id, UserId: null, IpAddress: "127.0.0.1", respondentToken, answers);

    public static AnswerRequest Picks(Guid questionId, params Guid[] optionIds) =>
        new(questionId, optionIds, TextValue: null, NumericValue: null);

    public static AnswerRequest Writes(Guid questionId, string text) =>
        new(questionId, SelectedOptionIds: null, text, NumericValue: null);

    public static AnswerRequest Enters(Guid questionId, decimal value) =>
        new(questionId, SelectedOptionIds: null, TextValue: null, value);
}
