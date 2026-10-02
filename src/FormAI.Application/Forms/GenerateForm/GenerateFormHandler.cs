using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public class GenerateFormHandler
{
    public readonly IFormGenerationService _generationService;
    public readonly IFormRepository _repository;

    public const string PrefixTitle = "Generated Form –";


    public GenerateFormHandler(IFormGenerationService formGenerationService,
        IFormRepository formRepository)
    {
        _generationService = formGenerationService;
        _repository = formRepository;
    }

    private static void ValidateForm(string title, string? description, GenerateFormRequest request)
    {
        if (title.Length > 255)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["title"] = ["Title must be at most 255 characters."]
            });
        }

        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceText"] = ["Source text is required to generate a form."]
            });
        }

        if (request.SourceText.Length > FormSourceContent.MaxSourceTextLength)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceText"] = [$"Source text must be at most {FormSourceContent.MaxSourceTextLength} characters."]
            });
        }

        if (description?.Length > 1024)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["description"] = ["Description must be at most 1024 characters."]
            });

        if (request.ExpiresAt <= DateTime.UtcNow)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["expiresAt"] = ["The expiry must be in the future."]
            });
    }

    public async Task<GenerateFormResponse> HandleAsync(GenerateFormRequest request,
    Guid requestingUserId,
    CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"{PrefixTitle} {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
            : request.Title.Trim();

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        ValidateForm(title, description, request);

        var sourceItems = new List<SourceItem>()
        {
            new SourceItem(request.SourceText, SourceType.Text)
        };

        var parameters = new GenerationParameters(request.QuestionCount,
        request.AllowedTypes, request.DifficultyLevel,
        request.IsGraded);

        var generatedQuestions = await _generationService.GenerateAsync(request.SourceText, parameters, requestingUserId, cancellationToken);

        GeneratedQuestionsValidator.Validate(generatedQuestions);

        var form = Form.Create(
            title: title,
            description: description ?? string.Empty,
            createdBy: requestingUserId,
            sourceType: SourceType.Text,
            isPublic: false,
            expiresAt: request.ExpiresAt,
            showResultsAfterSubmit: request.ShowResultsAfterSubmit,
            isGraded: request.IsGraded
        );

        var sourceContents = sourceItems.Select((s, i) =>
            FormSourceContent.Create(form.Id,
            form.SourceType, s.SourceText, i + 1, s.FileName)).ToList();

        var questions = generatedQuestions.Select((q, i) =>
        {
            FormQuestion question = CreateFormQuestion(request, q, i, form);
            return question;
        }).ToList();

        form.ReplaceQuestions(questions);
        form.AddSourceContent(sourceContents);

        // Discards anything the AI marked when the owner asked for an ungraded form.
        form.ClearGradingIfUngraded();

        await _repository.AddAsync(form, cancellationToken);
        return CreateFormResponse(form);

    }

    private static GenerateFormResponse CreateFormResponse(Form form)
    {
        return new GenerateFormResponse(form.Id,
                form.Title,
                form.SourceType,
                form.CreatedAt,
                form.Questions
                    .OrderBy(q => q.Order)
                    .Select(q =>
                    new GeneratedQuestionResponse(q.Id,
                        q.Text,
                        q.Type,
                        q.Order,
                        q.IsRequired,
                        q.AiGenerated,
                        q.Points,
                        q.CorrectAnswer,
                        q.Options.OrderBy(o => o.Order).
                        Select(o => new GeneratedOptionResponse(o.Id, o.Text, o.Order, o.IsCorrect)).ToList()
                        )).ToList());
    }

    private static FormQuestion CreateFormQuestion(GenerateFormRequest request, GeneratedQuestion q, int order, Form form)
    {
        // A generated question starts at the default; the owner changes it in the editor.
        var points = request.IsGraded ? Form.DefaultQuestionPoints : (int?)null;

        var question = FormQuestion.Create(form.Id, q.Text, q.Type, order + 1,
        q.IsRequired, true, points, q.CorrectAnswer);


        var options = new List<QuestionOption>();
        var alreadyHasCorrectOption = false;

        foreach (var (o, optionIndex) in q.Options.Select((o, index) => (o, index)))
        {
            var isCorrect = o.IsCorrect;

            // A Single question has at most one answer key: keep the first option
            // the AI marked and clear every other one it marked.
            if (question.Type == QuestionType.Single && isCorrect == true)
            {
                isCorrect = !alreadyHasCorrectOption;
                alreadyHasCorrectOption = true;
            }

            options.Add(QuestionOption.Create(question.Id, o.Text, optionIndex + 1, isCorrect));
        }

        question.SetOptions(options);
        return question;
    }

    public string CombineItems(List<SourceItem> items)
    {
        var parts = items.Select((item, i) =>
        {
            var label = $"Source {i + 1}, Type :{item.SourceType} ";
            if (!string.IsNullOrWhiteSpace(item.FileName))
                label += $" FileName: {item.FileName}";

            label += $", Content: {item.SourceText} ";

            return label;
        });

        return string.Join(Environment.NewLine, parts);
    }
}