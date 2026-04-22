
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.UpdateQuestions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Forms.UpdateQuestions;

public class UpdateQuestionsHandler
{
    private readonly IFormRepository _forms;

    public UpdateQuestionsHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(
        UpdateQuestionsRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException($"Form not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");
        
        var questions = request.Questions.Select(q =>
        {
            var question = FormQuestion.Create(request.FormId,
                q.Text, q.Type, q.Order, q.IsRequired, q.AiGenerated, q.Points, q.CorrectAnswer);

            var options = q.options.Select(o => QuestionOption.Create(question.Id, o.Text, o.Order, o.IsCorrect))
                        .ToList();
            
            question.SetOptions(options);

            return question;
        }).ToList();

        form.ReplaceQuestions(questions);

        await _forms.UpdateAsync(form, cancellationToken);
    }
}