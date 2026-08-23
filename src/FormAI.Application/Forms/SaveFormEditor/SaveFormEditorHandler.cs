using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.UpdateQuestions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.SaveFormEditor;

public class SaveFormEditorHandler
{
    private readonly IFormRepository _forms;

    public SaveFormEditorHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task HandleAsync(SaveFormEditorRequest request, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException("Form not found.");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form.");

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.");

        if (request.Title.Length > 255)
            throw new ArgumentException("Title must be at most 255 characters.");

        if (request.Description?.Length > 1024)
            throw new ArgumentException("Description must be at most 1024 characters.");

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];

            if (q.Type is QuestionType.Single or QuestionType.Multiple)
                QuestionOptionValidator.Validate(i, q.Options.Select(o => o.Text).ToList(), errors);
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        form.Update(request.Title.Trim(), request.Description?.Trim() ?? string.Empty, request.IsPublic,
            form.ExpiresAt, form.ShowResultsAfterSubmit);

        var questions = request.Questions.Select(q =>
        {
            var question = FormQuestion.Create(request.FormId,
                q.Text, q.Type, q.Order, q.IsRequired, q.AiGenerated, q.Points, q.CorrectAnswer);

            var options = q.Options.Select(o => QuestionOption.Create(question.Id, o.Text.Trim(), o.Order, o.IsCorrect))
                        .ToList();

            question.SetOptions(options);
            return question;
        }).ToList();

        form.ReplaceQuestions(questions);

        await _forms.UpdateAsync(form, cancellationToken);
    }
}