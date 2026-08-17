using System.Formats.Asn1;
using System.Reflection.Metadata;
using FormAI.Application.AI;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public class GenerateFormHandler
{
    public readonly IFormGenerationService _generationService;
    public readonly IFormRepository _repository;

    public GenerateFormHandler(IFormGenerationService formGenerationService,
        IFormRepository formRepository)
    {
        _generationService = formGenerationService;
        _repository = formRepository;
    }

    public async Task<GenerateFormResponse> HandleAsync(GenerateFormRequest request,
    Guid requestingUserId,
    CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"Generated Form – {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
            : request.Title.Trim();

        if (title.Length > 255)
            throw new ArgumentException("Title must be at most 255 characters.");

        if (string.IsNullOrWhiteSpace(request.SourceText))
            throw new ArgumentException("Source text is required to generate a form.");

        var sourceItems = new List<SourceItem>()
        {
            new SourceItem(request.SourceText, SourceType.Text)
        };

        var parameters = new GenerationParameters(request.QuestionCount,
        request.AllowedTypes, request.DifficultyLevel,
        request.IncludeCorrectAnswers);

        var generatedQuestions = await _generationService.GenerateAsync(request.SourceText, parameters, cancellationToken);

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        if (description?.Length > 1024)
            throw new ArgumentException("Description must be at most 1024 characters.");

        var form = Form.Create(
            title: title,
            description: description ?? string.Empty,
            createdBy: requestingUserId,
            sourceType: SourceType.Text,
            isPublic: false,
            expiresAt: DateTime.UtcNow.AddDays(15),
            showResultsAfterSubmit: false
        );

        var sourceContents = sourceItems.Select((s, i) =>
            FormSourceContent.Create(form.Id,
            form.SourceType, s.SourceText, i + 1, s.FileName)).ToList();


        var questions = generatedQuestions.Select((q, i) =>
        {
            var question = FormQuestion.Create(form.Id, q.Text, q.Type, i + 1,
            q.IsRequired, true, q.Points, q.CorrectAnswer);

            var options = q.Options
            .Select((o, i) => QuestionOption.Create(question.Id, o.Text, i + 1, o.IsCorrect))
            .ToList();

            question.SetOptions(options);
            return question;
        }).ToList();

        form.ReplaceQuestions(questions);
        await _repository.AddAsync(form, cancellationToken);

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