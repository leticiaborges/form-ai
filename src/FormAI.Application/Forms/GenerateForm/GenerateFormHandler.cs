using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Common.Files;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public class GenerateFormHandler
{
    public readonly IFormGenerationService _generationService;
    public readonly IFormRepository _repository;
    private readonly ISourceTextExtractor _extractor;

    public const string PrefixTitle = "Generated Form –";

    public const int MinSourceTextLength = 100;
    public const int MaxFileNameLength = 255;

    public GenerateFormHandler(IFormGenerationService formGenerationService,
        IFormRepository formRepository,
        ISourceTextExtractor extractor)
    {
        _generationService = formGenerationService;
        _repository = formRepository;
        _extractor = extractor;
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

        if (request.SourceText?.Length > FormSourceContent.MaxSourceTextLength)
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

        if (request.QuestionCount is < 1 or > 20)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["questionCount"] = ["The number of questions must be between 1 and 20."]
            });
    }

    private (List<SourceItem> Items, List<GenerationSource> Sources) BuildSources(GenerateFormRequest request)
    {
        var pasted = request.SourceText?.Trim() ?? string.Empty;

        if (pasted.Length == 0 && request.File is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceText"] = ["Paste some text or upload a file to generate a form."]
            });
        }

        var items = new List<SourceItem>();
        var sources = new List<GenerationSource>();

        if (pasted.Length > 0)
        {
            items.Add(new SourceItem(pasted, SourceType.Text));
            sources.Add(new TextSource(pasted));
        }

        var hasPdf = false;
        if (request.File != null)
        {
            var file = request.File;
            var fileName = FileHelper.SanitizeFileName(file.FileName);

            if (FileHelper.IsPdf(fileName))
            {
                FileHelper.EnsurePdfIsAcceptable(file.Content);
                hasPdf = true;

                items.Add(new SourceItem(string.Empty, SourceType.Pdf, fileName));
                sources.Add(new PdfSource(file.Content));
            }
            else
            {
                var extracted = _extractor.Extract(fileName, file.Content);
                items.Add(new SourceItem(extracted, SourceTypeOf(fileName), fileName));
                sources.Add(new TextSource(extracted));
            }
        }

        var total = items.Sum(i => i.SourceText.Length);
        if (total > FormSourceContent.MaxSourceTextLength)
            throw TextTooLong();

        if (!hasPdf && total < MinSourceTextLength)
        {
            throw new ValidationException(ValidationErrorCode.SourceTextTooShort,
             $"The text is too short to make questions from. Use at least {MinSourceTextLength} characters.");
        }

        return (items, sources);
    }

    private static ValidationException TextTooLong() =>
        new(ValidationErrorCode.SourceTextTooLong,
            $"The text is too long. The maximum is {FormSourceContent.MaxSourceTextLength} characters, pasted text and file together.");

    private static SourceType SourceTypeOf(string fileName) =>
            Path.GetExtension(fileName).ToLowerInvariant() switch
            {
                ".docx" => SourceType.Word,
                ".pptx" => SourceType.Presentation,
                _ => SourceType.Text
            };

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

        var (sourceItems, generationSources) = BuildSources(request);
        var sourceText = string.Join("\n\n", sourceItems.Select(s => s.SourceText));

        var parameters = new GenerationParameters(request.QuestionCount,
        request.AllowedTypes, request.DifficultyLevel,
        request.IsGraded);

        var generatedQuestions = await _generationService.GenerateAsync(generationSources, parameters, requestingUserId, cancellationToken);

        GeneratedQuestionsValidator.Validate(generatedQuestions);

        var form = Form.Create(
            title: title,
            description: description ?? string.Empty,
            createdBy: requestingUserId,
            sourceType: request.File is null ? SourceType.Text : sourceItems[sourceItems.Count - 1].SourceType,
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
}