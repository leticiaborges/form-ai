using FormAI.Application.Forms.SaveFormEditor;
using FormAI.Domain.Entities;

namespace FormAI.UnitTests.Forms;

public static class SaveFormTestsHelper
{
    public static QuestionInput QuestionInput(FormQuestion formQuestion)
    {
        return new QuestionInput(formQuestion.Id, formQuestion.Text,
            formQuestion.Type, formQuestion.Order, formQuestion.IsRequired,
            formQuestion.AiGenerated, formQuestion.Points,
            formQuestion.CorrectAnswer, formQuestion.Options.Select(a => new OptionInput(a.Id, a.Text, a.Order, a.IsCorrect)).ToList());
    }
}