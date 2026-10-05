# Slice 13: Endpoint flow for text files (`.txt`, `.docx`, `.pptx`)

Covers EP-3, EP-6, EP-7 and EP-8 of [`phase-37.md`](./phase-37.md). The extractors from slices 10-12 are finally **called**: `POST /api/forms/generate` accepts one optional `file` next to the pasted text, extracts it, checks the combined text, generates, and saves the form with one `FormSourceContent` row per source. PDF is slice 14; the frontend picker is slice 15.

## Decisions

| Topic | Decision |
|---|---|
| Wire format | A new optional multipart field `file` (`IFormFile?`) on `GenerateFormDataRequest`. More than one file part answers 400 with a `file` field error. The controller turns the `IFormFile` into bytes (`SourceFile(FileName, Content)`), so `Application` stays free of ASP.NET types. |
| Request shape | `GenerateFormRequest` gets a last, optional parameter `SourceFile? File = null`. Existing callers and tests compile unchanged. |
| Order (EP-3) | Rate limit (middleware, already first) → title, description and expiry → **pasted text over 30,000, before touching the file** → extract → combined limits → generate → save. Cheap checks run before the expensive ones. |
| Combined text | The sources in order (pasted first, file second), each trimmed, joined with a blank line and sent to the model as **one string**. The 100 to 30,000 limits count the **sum of the sources' lengths**, not the separator. The old 30,000 cap on pasted text alone stays as an early check. |
| Neither text nor file | Keeps the existing field error on `sourceText` (message changes). The new codes below are for the length limits. |
| New codes | `SourceTextTooShort` and `SourceTextTooLong` in `ValidationErrorCode` (AI-9), both 400 through `ValidationException(code, message)`. They replace the old `sourceText` field error for "too long". |
| Minimum 100 | Now enforced for every request, pasted-only included. This breaks short test strings and the frontend's old "min 1": fixed in this slice (sections 3 and 4). Slice 14 lifts the minimum when a PDF is present. |
| Persistence | One `FormSourceContent` per source, `Order` from 1: pasted (`Text`, no file name) then the file (extracted text plus sanitized `FileName`). Each row now carries **its own** `SourceType`, not the form's. |
| `Form.SourceType` | `Text` if no file, otherwise the file's type. A txt file is `Text`, a docx is `Word`, a pptx is the new `Presentation = 4`. The column is an `int`, so there is **no migration**. |
| File name (SC-2) | Last path segment only (`/` and `\`), control characters removed, trimmed, at most 255 characters keeping the **end** so the extension survives. Stored, never interpolated into the prompt. |
| Dead code | `GenerateFormHandler.CombineItems` is deleted. Nothing calls it, and it puts the file name into a prompt-shaped string, which SC-2 forbids. |
| Extractor fix | `SourceTextExtractor` currently accepts `.doc` and `.ppt` and sends them to the pptx reader (`_ =>` branch), which can only fail. Back to `.txt .docx .pptx`, as slice 12 specified. The message stops telling the user to use `.pdf` until slice 14 makes that true. |
| Disconnect (EP-7) | The controller's `CancellationToken` is already `RequestAborted` and the handler passes it on. What was missing: a cancelled request fell into the middleware's `_` branch and was logged as an unhandled 500. It now ends quietly with status 499. A timeout inside the gateway client is still `GenerationUnavailable`; only a caller's cancellation reaches this branch. |
| EP-6 | Unchanged: private, expiry as chosen, grading rules as before. Existing tests cover it. |
| EP-8 | No code change. Generated text is rendered as React text, never as HTML. Checked by a grep (section 6). |

---

## 1. Backend

### 1.1 `src/FormAI.Domain/Enums/SourceType.cs`

```csharp
namespace FormAI.Domain.Enums;

public enum SourceType
{
    Text = 1,
    Word = 2,
    Pdf = 3,
    Presentation = 4
}
```

### 1.2 `src/FormAI.Application/Common/Exceptions/ValidationErrorCode.cs`

Add two members at the end:

```csharp
    SourceFileTooLarge,
    SourceTextTooShort,
    SourceTextTooLong,
}
```

### 1.3 New `src/FormAI.Application/Forms/GenerateForm/SourceFile.cs`

```csharp
namespace FormAI.Application.Forms.GenerateForm;

// An uploaded file as the use case sees it: a name and bytes, no ASP.NET types.
public record SourceFile(string FileName, byte[] Content);
```

### 1.4 `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormRequest(
    string? Title,
    string? Description,
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    DifficultyLevel DifficultyLevel,
    bool IsGraded,
    bool ShowResultsAfterSubmit,
    DateTime ExpiresAt,
    SourceFile? File = null
);
```

### 1.5 `src/FormAI.Application/Forms/GenerateForm/SourceItem.cs`

`FileName` becomes nullable, so a pasted source stores `null` and not `""`:

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record SourceItem(
    string SourceText,
    SourceType SourceType,
    string? FileName = null
);
```

### 1.6 `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs`

Replace the whole file:

```csharp
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
    private readonly ISourceTextExtractor _extractor;

    public const string PrefixTitle = "Generated Form –";

    // Pasted plus extracted text, counted together.
    public const int MinSourceTextLength = 100;

    // Room for the editor's file name column (512) with the unicode a name may carry.
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

    // Pasted text first, then the file. Cheap checks run before the file is read.
    private List<SourceItem> BuildSourceItems(GenerateFormRequest request)
    {
        var pasted = request.SourceText.Trim();

        if (pasted.Length == 0 && request.File is null)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["sourceText"] = ["Paste some text or upload a file to generate a form."]
            });
        }

        // The combined text can't be shorter than the pasted part, so no need to read the file.
        if (pasted.Length > FormSourceContent.MaxSourceTextLength)
            throw TextTooLong();

        var items = new List<SourceItem>();

        if (pasted.Length > 0)
            items.Add(new SourceItem(pasted, SourceType.Text));

        if (request.File is { } file)
        {
            var fileName = SanitizeFileName(file.FileName);
            var extracted = _extractor.Extract(fileName, file.Content);
            items.Add(new SourceItem(extracted, SourceTypeOf(fileName), fileName));
        }

        var total = items.Sum(i => i.SourceText.Length);

        if (total > FormSourceContent.MaxSourceTextLength)
            throw TextTooLong();

        if (total < MinSourceTextLength)
        {
            throw new ValidationException(ValidationErrorCode.SourceTextTooShort,
                $"The text is too short to make questions from. Use at least {MinSourceTextLength} characters.");
        }

        return items;
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

    // Last path segment only, no control characters, and the end of the name kept so the extension survives.
    private static string SanitizeFileName(string? name)
    {
        name ??= string.Empty;

        var lastSeparator = name.LastIndexOfAny(['/', '\\']);
        var clean = new string(name[(lastSeparator + 1)..].Where(c => !char.IsControl(c)).ToArray()).Trim();

        return clean.Length > MaxFileNameLength ? clean[^MaxFileNameLength..] : clean;
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

        var sourceItems = BuildSourceItems(request);
        var sourceText = string.Join("\n\n", sourceItems.Select(s => s.SourceText));

        var parameters = new GenerationParameters(request.QuestionCount,
        request.AllowedTypes, request.DifficultyLevel,
        request.IsGraded);

        var generatedQuestions = await _generationService.GenerateAsync(sourceText, parameters, requestingUserId, cancellationToken);

        GeneratedQuestionsValidator.Validate(generatedQuestions);

        var form = Form.Create(
            title: title,
            description: description ?? string.Empty,
            createdBy: requestingUserId,
            sourceType: request.File is null ? SourceType.Text : sourceItems[^1].SourceType,
            isPublic: false,
            expiresAt: request.ExpiresAt,
            showResultsAfterSubmit: request.ShowResultsAfterSubmit,
            isGraded: request.IsGraded
        );

        var sourceContents = sourceItems.Select((s, i) =>
            FormSourceContent.Create(form.Id,
            s.SourceType, s.SourceText, i + 1, s.FileName)).ToList();

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
```

Differences from today, so you can diff by eye: the new constructor parameter, `ValidateForm` without the two `sourceText` checks, `BuildSourceItems` / `TextTooLong` / `SourceTypeOf` / `SanitizeFileName`, `sourceText` joined from the items, `sourceType` and `s.SourceType` in the creation, and `CombineItems` gone. `CreateFormResponse` and `CreateFormQuestion` are untouched.

`Infrastructure/DependencyInjection.cs` needs no change: the handler is `AddScoped` and `ISourceTextExtractor` is already a singleton.

### 1.7 `src/FormAI.Infrastructure/Files/SourceTextExtractor.cs`

Replace the type check (lines 23-29) so it matches what is implemented:

```csharp
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".txt" or ".docx" or ".pptx"))
        {
            throw new Application.Common.Exceptions.ValidationException(ValidationErrorCode.SourceFileUnsupported,
                "This file type is not supported. Use a .docx, .pptx or .txt file.");
        }
```

The `switch` below it stays. Slice 14 puts `.pdf` back into the message.

### 1.8 `src/FormAI.API/Contracts/GenerateFormDataRequest.cs`

Add one property and the `using`:

```csharp
using FormAI.Domain.Enums;
using Microsoft.AspNetCore.Http;

public class GenerateFormDataRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string SourceText { get; init; } = string.Empty;
    public IFormFile? File { get; init; }
    public int QuestionCount { get; init; }
    public QuestionType[]? AllowedTypes { get; init; }
    public DifficultyLevel DifficultyLevel { get; init; }
    public bool IsGraded { get; init; }
    public bool ShowResultsAfterSubmit { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}
```

### 1.9 `src/FormAI.API/Controllers/FormsController.cs`

Add `using FormAI.Application.Common.Exceptions;`, then replace the `Generate` action:

```csharp
    // POST /api/forms/generate
    // Text, one file (.txt, .docx, .pptx), or both. The file is read into memory and never stored.
    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(GenerateMaxRequestBytes)]
    public async Task<IActionResult> Generate([FromForm] GenerateFormDataRequest form,
        CancellationToken cancellationToken)
    {
        // Counts every file part, whatever field name it came under.
        if (Request.Form.Files.Count > 1)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["Upload one file at most."]
            });
        }

        SourceFile? file = null;
        if (form.File is { } upload)
        {
            await using var stream = upload.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            file = new SourceFile(upload.FileName, buffer.ToArray());
        }

        var request = new GenerateFormRequest(
        form.Title,
        form.Description,
        form.SourceText,
        SourceType.Text,
        null,
        form.QuestionCount,
        form.AllowedTypes,
        form.DifficultyLevel,
        form.IsGraded,
        form.ShowResultsAfterSubmit,
        form.ExpiresAt.UtcDateTime,
        file);

        var response = await _generateForm.HandleAsync(request, CurrentUserId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.FormId }, response);
    }
```

The request limit (11 MB) stays on this action only. A body over it is rejected by Kestrel with 413 before any of this runs; a file between 10 and about 10.9 MB gets `SourceFileTooLarge` from the extractor.

### 1.10 `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs` (EP-7)

In `InvokeAsync`, add a catch **before** the general one:

```csharp
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client went away (closed the tab, cancelled). Nobody is listening, and it isn't a fault.
            _logger.LogInformation("Request cancelled by the client: {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
                context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
```

`TaskCanceledException` derives from `OperationCanceledException`, so one catch covers both. The `when` filter keeps a timeout that is *not* the client's from being swallowed.

---

## 2. Backend tests

### 2.1 `tests/FormAI.UnitTests/Forms/GenerateFormTests.cs`: keep it green

1. Add a constant at the top of the class and use it as the default source (the old 14-character strings are under the new minimum):

```csharp
    private const string DefaultSource =
        "Paris is the capital of France. Rome is the capital of Italy. Madrid is the capital of Spain. Lisbon is the capital of Portugal.";
```

2. Replace every `"SourceTextTest"` in the file with `DefaultSource` (the `CreateRequest` default, the two `GenerateAsync` received-asserts, and the `InvalidRequests` rows). `DefaultSource` is a `const`, so it works as a default parameter value.
3. The constructor now needs the extractor:

```csharp
    private readonly ISourceTextExtractor _extractor = Substitute.For<ISourceTextExtractor>();
    ...
        _handler = new GenerateFormHandler(_generationService, _forms, _extractor);
```

Everything else in the file stays: the `sourceText` empty row still expects the `sourceText` key.

### 2.2 New `tests/FormAI.UnitTests/Forms/GenerateFormSourcesTests.cs`

```csharp
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.GenerateForm;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using NSubstitute;

namespace FormAI.UnitTests.Forms;

public class GenerateFormSourcesTests
{
    private readonly IFormGenerationService _generation = Substitute.For<IFormGenerationService>();
    private readonly IFormRepository _forms = Substitute.For<IFormRepository>();
    private readonly ISourceTextExtractor _extractor = Substitute.For<ISourceTextExtractor>();
    private readonly GenerateFormHandler _handler;

    private static readonly byte[] Bytes = [1, 2, 3];

    public GenerateFormSourcesTests()
    {
        _handler = new GenerateFormHandler(_generation, _forms, _extractor);

        _generation.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([new GeneratedQuestion("Why?", QuestionType.Text, true, null, [])]);
    }

    private static string Text(int length) => new('a', length);

    private static GenerateFormRequest Request(string sourceText, SourceFile? file = null) =>
        new("Title", null, sourceText, SourceType.Text, null, 1, null, DifficultyLevel.Medium,
            false, false, DateTime.UtcNow.AddDays(7), file);

    private Form CaptureSavedForm()
    {
        Form? saved = null;
        _ = _forms.AddAsync(Arg.Do<Form>(f => saved = f), Arg.Any<CancellationToken>());
        return saved!; // read after HandleAsync through the closure below
    }

    private async Task<Form> HandleAsync(GenerateFormRequest request, CancellationToken ct = default)
    {
        Form? saved = null;
        _ = _forms.AddAsync(Arg.Do<Form>(f => saved = f), Arg.Any<CancellationToken>());
        await _handler.HandleAsync(request, Guid.NewGuid(), ct);
        Assert.NotNull(saved);
        return saved;
    }

    private async Task AssertNothingGeneratedOrSaved()
    {
        await _generation.DidNotReceive().GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _forms.DidNotReceive().AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());
    }

    // ---- what is sent and saved ----

    [Fact]
    public async Task FileOnly_SendsTheExtractedText_AndSavesItWithTheFileName()
    {
        _extractor.Extract("notes.txt", Bytes).Returns(Text(150));

        var form = await HandleAsync(Request("", new SourceFile("notes.txt", Bytes)));

        await _generation.Received(1).GenerateAsync(Text(150), Arg.Any<GenerationParameters>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        var source = Assert.Single(form.SourceContents);
        Assert.Equal(Text(150), source.Content);
        Assert.Equal("notes.txt", source.FileName);
        Assert.Equal(1, source.Order);
        Assert.Equal(SourceType.Text, form.SourceType);
    }

    [Fact]
    public async Task PastedTextAndFile_AreSentTogetherAndSavedAsTwoOrderedSources()
    {
        _extractor.Extract("deck.pptx", Bytes).Returns("from the deck " + Text(100));

        var form = await HandleAsync(Request("  pasted " + Text(60) + "  ", new SourceFile("deck.pptx", Bytes)));

        var pasted = "pasted " + Text(60);
        await _generation.Received(1).GenerateAsync(pasted + "\n\n" + "from the deck " + Text(100),
            Arg.Any<GenerationParameters>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        Assert.Equal([1, 2], form.SourceContents.Select(s => s.Order));
        Assert.Equal([SourceType.Text, SourceType.Presentation], form.SourceContents.Select(s => s.SourceType));
        Assert.Equal([null, "deck.pptx"], form.SourceContents.Select(s => s.FileName));
        Assert.Equal(SourceType.Presentation, form.SourceType);
    }

    [Theory]
    [InlineData("a.docx", SourceType.Word)]
    [InlineData("a.DOCX", SourceType.Word)]
    [InlineData("a.pptx", SourceType.Presentation)]
    [InlineData("a.txt", SourceType.Text)]
    public async Task TheFormTakesTheTypeOfItsFile(string fileName, SourceType expected)
    {
        _extractor.Extract(fileName, Bytes).Returns(Text(150));

        var form = await HandleAsync(Request("", new SourceFile(fileName, Bytes)));

        Assert.Equal(expected, form.SourceType);
        Assert.Equal(expected, form.SourceContents.Single().SourceType);
    }

    [Fact]
    public async Task PastedTextOnly_StillSavesOneTextSourceWithoutAFileName()
    {
        var form = await HandleAsync(Request(Text(120)));

        var source = Assert.Single(form.SourceContents);
        Assert.Equal(SourceType.Text, source.SourceType);
        Assert.Null(source.FileName);
        _extractor.DidNotReceiveWithAnyArgs().Extract(default!, default!);
    }

    // ---- file name ----

    [Theory]
    [InlineData("C:\\Users\\me\\report.docx", "report.docx")]
    [InlineData("../../etc/notes.txt", "notes.txt")]
    [InlineData("bad\r\nname.txt", "badname.txt")]
    public async Task TheFileNameIsReducedToItsLastSegmentWithoutControlCharacters(string raw, string expected)
    {
        _extractor.Extract(expected, Bytes).Returns(Text(150));

        var form = await HandleAsync(Request("", new SourceFile(raw, Bytes)));

        Assert.Equal(expected, form.SourceContents.Single().FileName);
    }

    [Fact]
    public async Task ALongFileName_IsCutFromTheStart_SoTheExtensionSurvives()
    {
        var name = new string('x', 400) + ".txt";
        var expected = name[^GenerateFormHandler.MaxFileNameLength..];
        _extractor.Extract(expected, Bytes).Returns(Text(150));

        var form = await HandleAsync(Request("", new SourceFile(name, Bytes)));

        Assert.Equal(expected, form.SourceContents.Single().FileName);
        Assert.EndsWith(".txt", form.SourceContents.Single().FileName);
    }

    // ---- limits ----

    [Fact]
    public async Task NeitherTextNorFile_IsAFieldErrorOnSourceText()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(Request("   "), Guid.NewGuid()));

        Assert.Equal(["sourceText"], ex.Errors.Keys);
        await AssertNothingGeneratedOrSaved();
    }

    [Theory]
    [InlineData(99, 0)]    // pasted alone
    [InlineData(50, 49)]   // pasted plus file
    [InlineData(0, 99)]    // file alone
    public async Task CombinedTextUnder100_IsTooShort(int pasted, int extracted)
    {
        var file = extracted > 0 ? new SourceFile("a.txt", Bytes) : null;
        if (file is not null) _extractor.Extract("a.txt", Bytes).Returns(Text(extracted));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(Request(Text(pasted), file), Guid.NewGuid()));

        Assert.Equal(ValidationErrorCode.SourceTextTooShort, ex.Code);
        await AssertNothingGeneratedOrSaved();
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(50, 50)]
    [InlineData(0, 30_000)]
    [InlineData(10_000, 20_000)]
    public async Task CombinedTextFrom100To30000_IsAccepted(int pasted, int extracted)
    {
        var file = extracted > 0 ? new SourceFile("a.txt", Bytes) : null;
        if (file is not null) _extractor.Extract("a.txt", Bytes).Returns(Text(extracted));

        await HandleAsync(Request(Text(pasted), file));
    }

    [Theory]
    [InlineData(10_000, 20_001)]
    [InlineData(0, 30_001)]
    public async Task CombinedTextOver30000_IsTooLong(int pasted, int extracted)
    {
        _extractor.Extract("a.txt", Bytes).Returns(Text(extracted));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(Request(Text(pasted), new SourceFile("a.txt", Bytes)), Guid.NewGuid()));

        Assert.Equal(ValidationErrorCode.SourceTextTooLong, ex.Code);
        await AssertNothingGeneratedOrSaved();
    }

    [Fact]
    public async Task PastedTextOver30000_IsRejectedWithoutReadingTheFile()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(Request(Text(30_001), new SourceFile("a.txt", Bytes)), Guid.NewGuid()));

        Assert.Equal(ValidationErrorCode.SourceTextTooLong, ex.Code);
        _extractor.DidNotReceiveWithAnyArgs().Extract(default!, default!);
    }

    // ---- order of checks ----

    [Fact]
    public async Task AnExpiredRequest_IsRejectedBeforeTheFileIsRead()
    {
        var request = Request("", new SourceFile("a.txt", Bytes)) with { ExpiresAt = DateTime.UtcNow.AddDays(-1) };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _handler.HandleAsync(request, Guid.NewGuid()));

        Assert.Equal(["expiresAt"], ex.Errors.Keys);
        _extractor.DidNotReceiveWithAnyArgs().Extract(default!, default!);
    }

    [Fact]
    public async Task AnUnreadableFile_StopsTheRequestBeforeGeneratingOrSaving()
    {
        _extractor.Extract("a.docx", Bytes).Returns(_ => throw new ValidationException(
            ValidationErrorCode.SourceFileUnreadable, "The file could not be read or has no text in it."));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _handler.HandleAsync(Request(Text(200), new SourceFile("a.docx", Bytes)), Guid.NewGuid()));

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, ex.Code);
        await AssertNothingGeneratedOrSaved();
    }

    // ---- disconnect (EP-7) ----

    [Fact]
    public async Task TheCancellationTokenReachesTheGeneratorAndTheRepository()
    {
        using var cts = new CancellationTokenSource();

        await HandleAsync(Request(Text(120)), cts.Token);

        await _generation.Received(1).GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(),
            Arg.Any<Guid>(), cts.Token);
        await _forms.Received(1).AddAsync(Arg.Any<Form>(), cts.Token);
    }

    [Fact]
    public async Task WhenTheClientDisconnectsDuringGeneration_NothingIsSaved()
    {
        _generation.GenerateAsync(Arg.Any<string>(), Arg.Any<GenerationParameters>(),
            Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _handler.HandleAsync(Request(Text(120)), Guid.NewGuid()));

        await _forms.DidNotReceive().AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());
    }
}
```

Delete the unused `CaptureSavedForm` helper from that file before committing (it's a leftover; `HandleAsync` does the capturing).

### 2.3 `tests/FormAI.UnitTests/Files/SourceTextExtractorTests.cs`

Extend the unsupported-extension theory so `.doc`/`.ppt` can't slip into the pptx reader again:

```csharp
    [Theory]
    [InlineData("notes.doc")]
    [InlineData("notes.ppt")]
    [InlineData("notes.pdf")]
    [InlineData("notes")]
    public void Extract_Throws_SourceFileUnsupported_ForAnyOtherExtension(string fileName)
```

(Keep the existing body. If the theory already lists other names, keep them.)

---

## 3. Frontend (only what the new minimum breaks)

The picker, progress and error messages are slice 15. Today a pasted text under 100 characters would get a 400 with a code the page doesn't know, so mirror the new minimum now.

### 3.1 `frontend/src/pages/CreateFormPage.tsx`

```ts
const MIN_SOURCE_TEXT_LENGTH = 100;
const MAX_SOURCE_TEXT_LENGTH = 30_000;
```

and in the schema, replace `.min(1, "Source text must be filled.")`:

```ts
  sourceText: z
    .string()
    .min(MIN_SOURCE_TEXT_LENGTH, `Source text must be at least ${MIN_SOURCE_TEXT_LENGTH} characters long`)
    .max(
      MAX_SOURCE_TEXT_LENGTH,
      `Source text must be at most ${MAX_SOURCE_TEXT_LENGTH} characters long`,
    ),
```

If the frontend has a TypeScript union for the error `code` values, add `"SourceTextTooShort"` and `"SourceTextTooLong"` to it (search for `"SourceFileUnreadable"`; if it isn't there, there's no union and nothing to do).

### 3.2 `frontend/src/pages/CreateFormPage.test.tsx`

`SOURCE_TEXT` is 71 characters. Replace line 11:

```ts
const SOURCE_TEXT =
  "The quick brown fox jumps over the lazy dog, again and again and again. ".repeat(2);
```

and add one test next to the others:

```ts
it("asks for at least 100 characters of source text", async () => {
  const user = userEvent.setup();
  renderCreate();

  await user.type(screen.getByLabelText("Source content"), "too short");
  await user.click(screen.getByRole("button", { name: "Generate form" }));

  expect(
    await screen.findByText("Source text must be at least 100 characters long"),
  ).toBeInTheDocument();
});
```

---

## 4. e2e: `frontend/e2e/generate-form.spec.ts`

The existing test's `sourceText` is 57 characters. Lengthen it:

```ts
      sourceText:
        "Paris is the capital of France. The why: is just because. ".repeat(3),
```

Then add the file cases. Each uses a fresh user, because every request spends a rate-limit permit. The fake gateway answers the same canned draft whatever the text, so these test the API's flow, not the model.

```ts
const baseFields = () => ({
  questionCount: 2,
  difficultyLevel: "Medium",
  isGraded: "false",
  showResultsAfterSubmit: "false",
  expiresAt: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
});

const longText = "The mitochondria is the powerhouse of the cell. ".repeat(4);

test("a .txt file alone generates a form", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      file: { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from(longText) },
    },
  });

  expect(res.status(), await res.text()).toBe(201);
});

test("pasted text and a file together generate a form", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      sourceText: "Some extra context for the model to use.",
      file: { name: "notes.txt", mimeType: "text/plain", buffer: Buffer.from(longText) },
    },
  });

  expect(res.status(), await res.text()).toBe(201);
});

test("text that is too short, even with a file, answers SourceTextTooShort", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      file: { name: "tiny.txt", mimeType: "text/plain", buffer: Buffer.from("too short") },
    },
  });

  expect(res.status()).toBe(400);
  expect((await res.json()).code).toBe("SourceTextTooShort");
});

test("a file type that is not supported answers SourceFileUnsupported", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      file: { name: "old.doc", mimeType: "application/msword", buffer: Buffer.from(longText) },
    },
  });

  expect(res.status()).toBe(400);
  expect((await res.json()).code).toBe("SourceFileUnsupported");
});

test("a .docx that is not a real docx answers SourceFileUnreadable", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      file: { name: "fake.docx", mimeType: "application/octet-stream", buffer: Buffer.from(longText) },
    },
  });

  expect(res.status()).toBe(400);
  expect((await res.json()).code).toBe("SourceFileUnreadable");
});

test("two files answer 400 and never reach the gateway", async ({ request }) => {
  const auth = await signIn(request, await registerVerifiedUser(request));

  // Playwright's multipart option can't repeat a field name, so send two different names.
  const res = await request.post(`${API_URL}/api/forms/generate`, {
    headers: auth,
    multipart: {
      ...baseFields(),
      file: { name: "a.txt", mimeType: "text/plain", buffer: Buffer.from(longText) },
      other: { name: "b.txt", mimeType: "text/plain", buffer: Buffer.from(longText) },
    },
  });

  expect(res.status()).toBe(400);
  expect((await res.json()).errors.file).toBeDefined();
});
```

Check `registerVerifiedUser` / `signIn` are already imported at the top of the file (they are). If `Buffer` isn't typed in the e2e tsconfig, add `import { Buffer } from "node:buffer";`.

Any other e2e spec that calls `generate` with a short `sourceText` needs a 100-character text too. The earlier search found only this spec; run `grep -rn "sourceText" frontend/e2e` once more before finishing.

---

## 5. Docs

- **`CLAUDE.md`**
  - *Forms and questions*, the `POST /api/forms/generate` bullet: it now also takes an optional `file` (`.txt`, `.docx`, `.pptx`, at most one; a second file part answers 400 on `file`). Text or file is required. `GenerateFormDataRequest.File` is turned into bytes in the controller (`SourceFile`), never passed down as `IFormFile`. Replace "No file field exists yet."
  - Same area, add the flow and limits: order is rate limit → title/description/expiry → pasted text over 30,000 → extract → combined text (pasted plus extracted, summed, **100 to 30,000**) → generate → save. The new codes are `SourceTextTooShort` and `SourceTextTooLong`; `SourceText` empty with no file stays a `sourceText` field error. Replace the `SourceText is capped at ...` line.
  - Data model: a form has one `FormSourceContent` per source (pasted, then the file with its sanitized name). The row carries its own `SourceType`; `Form.SourceType` is `Text` or the file's type (`Presentation` is new). The original file is never stored.
  - Errors: the middleware ends a request cancelled by the client with status 499 and an info log, not a 500.
  - Remove any sentence saying the extractor is "not called by the generate flow".
- **`CONTEXT.md`**, *Source material*: the kinds become `Text`, `Word`, `Pdf`, `Presentation`, `Image`, `Url`; `Text`, `Word` (docx) and `Presentation` (pptx) can be supplied, plus txt as `Text`. *Extracted content*: now also says a form can hold several (pasted text plus one file), each with its file name.
- **`docs/known-gaps.md`**
  - *Generation from PDF, Word, image or URL*: `POST /api/forms/generate` accepts `.txt`, `.docx` and `.pptx` files next to the text; the frontend has no picker yet (slice 15) and PDF is slice 14. `PdfExtractor` and `UrlScraper` stay as they are. Drop "`GenerateFormHandler` hardcodes `SourceType.Text` and ignores the URL" only if it is no longer true; it still ignores `SourceUrl`.
  - *Multiple source items per form*: now there are up to two (pasted text and one file); remove the `CombineItems` mention. A source is a limit of the endpoint (one file), not of the model.
  - *Generation rate limit holds for one server only*: change the route from `/generate/text` to `/generate`, and replace the last sentence: the body limit for this route is 11 MB, set on the action. A larger body is a plain 413 without the `{ message, errors, code }` shape.
  - New row, *The 100-character minimum blocks short pasted text*: intended, but the frontend only shows the zod message, not a per-code message, until slice 15.
- **`docs/plans/phase-37.md`**: mark slice 13 done.
- ADRs: nothing. Combining sources into one string and the 499 are reversible and small.

---

## 6. Verify

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
cd frontend
npm test
npm run test:e2e -- generate-form
```

EP-8, no generated text is rendered as HTML:

```bash
grep -rn "dangerouslySetInnerHTML\|innerHTML" frontend/src
```

It must print nothing.

Manually, with `dotnet run --project src/FormAI.API` (and the fake or the real gateway), using Swagger or `curl`:

1. A real `.docx` and a real `.pptx` saved from Word and PowerPoint, alone: 201, and the editor shows questions about their content.
2. Pasted text plus a `.txt`: 201. In the database, `form_source_contents` has two rows, `order` 1 and 2, with `file_name` null for the first and the sanitized name for the second, and `forms.source_type` is the file's.
3. A `.pdf`: 400 `SourceFileUnsupported`, with a message that no longer offers `.pdf`.
4. A file over 10 MB (under 11): `SourceFileTooLarge`. Over 11 MB: Kestrel's 413.
5. **Disconnect.** Start a generation against the `[fake:slow]` marker (text containing `[fake:slow]`, padded to 100 characters), then cancel the request (Ctrl+C in `curl`). The API log shows one `Request cancelled by the client` line at information level, **no** `Unhandled exception` error, and `forms` has no new row.
6. Over-limit: 11 attempts in an hour still give the unchanged 429 body.

## Done when

- `POST /api/forms/generate` creates a form from a `.txt`, `.docx` or `.pptx`, alone or with pasted text, and saves one source row per source.
- The combined text is limited to 100 to 30,000 in that order of checks, with `SourceTextTooShort` / `SourceTextTooLong`; the file errors come back as their codes.
- A client that disconnects mid-generation saves nothing and is not logged as an error.
- `.doc` and `.ppt` answer `SourceFileUnsupported`.
- Unit tests, Vitest and the generate e2e spec pass; the docs name the new behaviour.
- `.pdf` is still unsupported and the frontend still sends text only.

## Suggested commits

1. `fix(infrastructure): reject .doc and .ppt instead of sending them to the pptx reader`
2. `feat(application): combine pasted text and an extracted file when generating a form`
3. `feat(api): accept one source file on POST /api/forms/generate`
4. `fix(api): end a request the client cancelled with 499 instead of logging a 500`
5. `feat(frontend): require 100 characters of source text`
6. `test: cover the file flow in unit and e2e tests`
7. `docs: record file-based generation in CLAUDE.md, CONTEXT.md and known-gaps`

(Commits 2 and 3 should land in one PR with 5: the minimum and the file field change the same contract.)
