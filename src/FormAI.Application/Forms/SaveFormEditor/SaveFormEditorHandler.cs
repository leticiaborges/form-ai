using FormAI.Application.Common.Exceptions;
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

        form.Update(request.Title.Trim(),
         request.Description?.Trim() ?? string.Empty,
          request.IsPublic,
            form.ExpiresAt, form.ShowResultsAfterSubmit);

        var diff = FormEditorDiffer.DiffQuestions(form.Questions, request.Questions);

        foreach (var question in diff.Removed)
            form.RemoveQuestion(question);

        foreach (var (question, input) in diff.Modified)
        {
            question.Update(input.Text.Trim(),
            input.Type, input.Order, input.IsRequired,
            input.AiGenerated, input.Points, input.CorrectAnswer?.Trim());
        }

        foreach (var input in diff.Added)
        {
            form.AddQuestion(BuildQuestion(form.Id, input));
        }

        foreach (var (question, input) in diff.KeepExisting)
            SyncOptions(question, input);

        await _forms.UpdateAsync(form, cancellationToken);
    }

    private static FormQuestion BuildQuestion(Guid formId,
    QuestionInput input)
    {
        var question = FormQuestion.Create(formId,
        input.Text.Trim(), input.Type,
        input.Order, input.IsRequired, input.AiGenerated,
        input.Points, input.CorrectAnswer?.Trim());

        question.SetOptions(input.Options.Select(o =>
        QuestionOption.Create(question.Id, o.Text.Trim(),
        o.Order, o.IsCorrect)).ToList());

        return question;
    }

    private static void SyncOptions(FormQuestion question,
    QuestionInput input)
    {
        var diff = FormEditorDiffer.DiffOptions(question.Options,
        input.Options);

        foreach (var option in diff.Removed)
            question.RemoveOption(option);

        foreach (var (option, o) in diff.Modified)
            option.Update(o.Text.Trim(), o.Order,
            o.IsCorrect);

        foreach (var o in diff.Added)
            question.AddOption(QuestionOption.Create(question.Id,
            o.Text.Trim(), o.Order, o.IsCorrect));
    }
}