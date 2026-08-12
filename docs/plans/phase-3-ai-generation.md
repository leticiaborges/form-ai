# Phase 3: AI Form Generation (Backend)

## Context

After login, the user submits one or more content sources — typed text, and optionally PDF/Word files — to the API and receives a complete draft form with AI-generated questions. The user can then review and modify those questions: renaming labels, reordering, adding questions manually, or deleting unwanted ones.

**Business rule:** Every form must be generated from content. There is no "create empty form" flow. At minimum, a text input is required.

**This phase covers only the backend.** All testing is done through Swagger or the `.http` file. The UI is Phase 4.

---

## What Already Exists — Do NOT Rewrite

| Already Exists | File |
|---|---|
| `IFormGenerationService` interface + `GenerationParameters`, `GeneratedQuestion`, `GeneratedOption` records | `src/FormAI.Application/AI/IFormGenerationService.cs` |
| `ClaudeFormGenerationService` stub (throws `NotImplementedException`) | `src/FormAI.Infrastructure/AI/ClaudeFormGenerationService.cs` |
| `GenerateFormRequest` record | `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs` |
| `SourceType` enum (Text=1, Word=2, Pdf=3, Image=4, Url=5) | `src/FormAI.Domain/Enums/SourceType.cs` |
| `PUT /api/forms/{id}/questions` + `UpdateQuestionsHandler` | handles all post-generation edits |
| `Form.ReplaceQuestions()` | `src/FormAI.Domain/Entities/Form.cs` |

---

## Architecture: Provider Abstraction

```
[Application layer — vendor-agnostic]
  IFormGenerationService          ← accepts plain string + parameters, returns questions

[Infrastructure layer — vendor-specific]
  ClaudeFormGenerationService     ← Anthropic implementation (Phase 3)
  OpenAiFormGenerationService     ← future: add class, change 1 DI line
```

To swap providers later: create `OpenAiFormGenerationService : IFormGenerationService`, then in `DependencyInjection.cs`:
```csharp
services.AddScoped<IFormGenerationService, OpenAiFormGenerationService>();
```
No other file needs to change.

---

## Design Decision: `FormSourceContent` as a 1:N Table

A form can have **multiple source content records** — e.g. one text block + two PDF files. Each record is one source item.

**Why not a column on `Form`?**
EF Core loads all columns by default. A `SourceContent` column on `forms` would silently load megabytes of text on every `GET /api/forms` (list) query. A separate table means EF only loads content when you explicitly call `.Include(f => f.SourceContents)`.

**Why 1:N instead of 1:0..1?**
A single generation can draw from multiple sources (text + PDF + PPTX). Each source is one row. The handler concatenates them all before calling the AI, so the `IFormGenerationService` interface stays unchanged — it always receives a single `string`.

**`FormSourceContent` columns:**

| Column | Type | Notes |
|---|---|---|
| `Id` | Guid (PK) | Own identity |
| `FormId` | Guid (FK → forms) | Parent form |
| `SourceType` | enum | Text / Pdf / Word / Image / Url |
| `Content` | `text` (no length limit) | Extracted text — files are never stored permanently, only their extracted text |
| `FileName` | string? | Original filename (e.g. `lecture.pdf`), null for typed text |
| `Order` | int | Order of this source among all sources for this form |

**No `AiPromptContext` field anywhere.** If the user wants to add context or instructions (e.g. "target high school students"), they type it directly as part of their text source.

**How multiple sources combine for the AI:**
The handler concatenates all source records in order:
```
[Source 1 — Text]
<typed text>

---

[Source 2 — Pdf: lecture.pdf]
<extracted text from PDF>
```
This combined string is passed to `IFormGenerationService.GenerateAsync()`. The interface does not change.

---

## Editing Questions After Generation

All editing operations reuse the existing `PUT /api/forms/{id}/questions` endpoint. It receives a complete list of questions and replaces the form's current list:

| Operation | How |
|---|---|
| Rename question/option | Send same list with changed `text` |
| Reorder | Send same list with different `order` values |
| Add manual question | Append to list with `aiGenerated: false` |
| Delete | Send list without that question |

No new endpoints needed for editing.

---

## Step-by-Step Implementation

---

### Step 1 — Remove `Context` from `GenerateFormRequest` and `GenerationParameters`

These two records already exist in the codebase. Remove `Context` from both — users now include any context directly in their text input.

**File:** `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs`

Replace the entire file with:
```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormRequest(
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    string DifficultyLevel,
    bool IncludeCorrectAnswers
);
```

**File:** `src/FormAI.Application/AI/IFormGenerationService.cs`

Replace `GenerationParameters` to remove `Context` (leave `GeneratedQuestion`, `GeneratedOption`, and the interface itself unchanged):
```csharp
public record GenerationParameters(
    int QuestionCount = 10,
    QuestionType[]? AllowedTypes = null,
    string DifficultyLevel = "medium",
    bool IncludeCorrectAnswers = false
);
```

---

### Step 2 — Add navigation property to the `Form` entity

**File:** `src/FormAI.Domain/Entities/Form.cs`

Add one navigation property (no new columns on this table):
```csharp
public List<FormSourceContent> SourceContents { get; private set; } = new();
```

No changes to `Form.Create()` or any other method.

---

### Step 3 — Create the `FormSourceContent` domain entity

**File to create:** `src/FormAI.Domain/Entities/FormSourceContent.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class FormSourceContent
{
    public Guid Id { get; private set; }
    public Guid FormId { get; private set; }
    public SourceType SourceType { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? FileName { get; private set; }
    public int Order { get; private set; }
    public Form? Form { get; private set; }

    private FormSourceContent() { }

    public static FormSourceContent Create(
        Guid formId,
        SourceType sourceType,
        string content,
        int order,
        string? fileName = null)
    {
        return new FormSourceContent
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            SourceType = sourceType,
            Content = content,
            FileName = fileName,
            Order = order
        };
    }
}
```

> `FileName` is null when the source is typed text. When it comes from a file upload, it holds the original filename so the user can see which file each record came from.

---

### Step 4 — Update `FormConfiguration.cs` for the new navigation property

**File:** `src/FormAI.Infrastructure/Data/Configurations/FormConfiguration.cs`

Add inside the `Configure` method, alongside the existing `HasMany` calls:
```csharp
builder.HasMany(f => f.SourceContents)
    .WithOne(s => s.Form)
    .HasForeignKey(s => s.FormId)
    .OnDelete(DeleteBehavior.Cascade);
```

> `OnDelete(DeleteBehavior.Cascade)`: when a form is deleted, all its source content rows are deleted automatically.

---

### Step 5 — Create `FormSourceContentConfiguration.cs`

**File to create:** `src/FormAI.Infrastructure/Data/Configurations/FormSourceContentConfiguration.cs`

```csharp
using FormAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FormAI.Infrastructure.Data.Configurations;

public class FormSourceContentConfiguration : IEntityTypeConfiguration<FormSourceContent>
{
    public void Configure(EntityTypeBuilder<FormSourceContent> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.SourceType)
            .IsRequired();

        builder.Property(s => s.Content)
            .IsRequired()
            .HasColumnType("text");  // PostgreSQL text — no length limit (~1 GB)

        builder.Property(s => s.FileName)
            .IsRequired(false)
            .HasMaxLength(512);

        builder.Property(s => s.Order)
            .IsRequired();
    }
}
```

> `HasColumnType("text")` explicitly maps to PostgreSQL's `text` type. Without it, EF Core might infer a default length cap.

---

### Step 6 — Register `FormSourceContent` in `AppDbContext`

**File:** `src/FormAI.Infrastructure/Data/AppDbContext.cs`

Add a `DbSet` alongside the existing ones:
```csharp
public DbSet<FormSourceContent> FormSourceContents => Set<FormSourceContent>();
```

---

### Step 7 — Create and apply the EF Core migration

Run from the **repo root**:

```bash
# Generate the migration file
dotnet ef migrations add AddFormSourceContents \
  --project src/FormAI.Infrastructure \
  --startup-project src/FormAI.API

# Apply to the database
dotnet ef database update \
  --project src/FormAI.Infrastructure \
  --startup-project src/FormAI.API
```

After the first command, open the generated file in `src/FormAI.Infrastructure/Migrations/` and verify it creates a `form_source_contents` table with `id` as PK and `form_id` as FK.

---

### Step 8 — Create `ClaudeSettings` configuration class

**File to create:** `src/FormAI.Infrastructure/AI/ClaudeSettings.cs`

```csharp
namespace FormAI.Infrastructure.AI;

public class ClaudeSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-opus-4-5";
    public int MaxTokens { get; set; } = 4096;
}
```

---

### Step 9 — Add Claude config to `appsettings` files

**`src/FormAI.API/appsettings.json`** (no secret here, just defaults):
```json
"Claude": {
  "Model": "claude-opus-4-5",
  "MaxTokens": 4096
}
```

**`src/FormAI.API/appsettings.Development.json`** (never commit this key to git):
```json
"Claude": {
  "ApiKey": "sk-ant-your-actual-key-here"
}
```

---

### Step 10 — Register `ClaudeSettings` and `HttpClient` in DI

**File:** `src/FormAI.Infrastructure/DependencyInjection.cs`

Add **before** the existing `IFormGenerationService` line:

```csharp
services.Configure<ClaudeSettings>(configuration.GetSection("Claude"));

services.AddHttpClient("claude", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});
```

> `AddHttpClient` registers a named HTTP client with pre-configured headers. `IHttpClientFactory` (built into ASP.NET Core) manages connection pooling efficiently. You get an instance by calling `_httpClientFactory.CreateClient("claude")`.

---

### Step 11 — Implement `ClaudeFormGenerationService`

**File:** `src/FormAI.Infrastructure/AI/ClaudeFormGenerationService.cs`

Replace the entire file:

```csharp
using System.Text;
using System.Text.Json;
using FormAI.Application.AI;
using FormAI.Domain.Enums;
using Microsoft.Extensions.Options;

namespace FormAI.Infrastructure.AI;

public class ClaudeFormGenerationService : IFormGenerationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ClaudeSettings _settings;

    public ClaudeFormGenerationService(
        IHttpClientFactory httpClientFactory,
        IOptions<ClaudeSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<GeneratedQuestion>> GenerateAsync(
        string sourceText,
        GenerationParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var requestBody = new
        {
            model = _settings.Model,
            max_tokens = _settings.MaxTokens,
            system = BuildSystemPrompt(parameters),
            messages = new[]
            {
                new { role = "user", content = BuildUserPrompt(sourceText, parameters) }
            }
        };

        var client = _httpClientFactory.CreateClient("claude");
        client.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("v1/messages", httpContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseResponse(responseJson);
    }

    private static string BuildSystemPrompt(GenerationParameters parameters)
    {
        var allowedTypes = parameters.AllowedTypes is { Length: > 0 }
            ? string.Join(", ", parameters.AllowedTypes)
            : "Single, Multiple, Text, Numeric";

        return $"""
            You are a quiz/form generator. Given source text, generate exactly {parameters.QuestionCount} questions.
            Allowed question types: {allowedTypes}.
            Difficulty: {parameters.DifficultyLevel}.

            Return ONLY a valid JSON object with this exact structure. No markdown, no explanation, just the JSON:
            {{
              "questions": [
                {{
                  "text": "question text here",
                  "type": "Single",
                  "isRequired": true,
                  "points": null,
                  "correctAnswer": null,
                  "options": [
                    {{ "text": "option A", "isCorrect": true }},
                    {{ "text": "option B", "isCorrect": false }}
                  ]
                }}
              ]
            }}

            Rules:
            - "Single" and "Multiple" questions MUST have at least 2 options.
            - "Text" and "Numeric" questions MUST have an empty options array: [].
            - "Mark correct option(s) with isCorrect: true."
            - For Numeric questions, correctAnswer should be the number as a string (e.g. "42") or null.
            - For Text questions, correctAnswer is a sample/expected answer string or null.
            """;
    }

    private static string BuildUserPrompt(string sourceText, GenerationParameters parameters)
        => $"Generate {parameters.QuestionCount} questions based on this content:\n\n{sourceText}";

    private static IReadOnlyList<GeneratedQuestion> ParseResponse(string responseJson)
    {
        // The Anthropic API response: { "content": [{ "text": "..." }] }
        using var apiDoc = JsonDocument.Parse(responseJson);
        var aiReplyText = apiDoc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString()
            ?? throw new InvalidOperationException("Claude returned empty content.");

        // Claude's reply is itself a JSON object (we instructed it to return only JSON)
        using var questionDoc = JsonDocument.Parse(aiReplyText);
        var questionsArray = questionDoc.RootElement.GetProperty("questions");

        var result = new List<GeneratedQuestion>();
        foreach (var q in questionsArray.EnumerateArray())
        {
            var type = Enum.Parse<QuestionType>(q.GetProperty("type").GetString()!);

            var options = q.GetProperty("options").EnumerateArray()
                .Select(o => new GeneratedOption(
                    Text: o.GetProperty("text").GetString()!,
                    IsCorrect: o.GetProperty("isCorrect").GetBoolean()))
                .ToList();

            result.Add(new GeneratedQuestion(
                Text: q.GetProperty("text").GetString()!,
                Type: type,
                IsRequired: q.GetProperty("isRequired").GetBoolean(),
                Points: q.TryGetProperty("points", out var pts) && pts.ValueKind != JsonValueKind.Null
                    ? pts.GetDecimal() : null,
                CorrectAnswer: q.TryGetProperty("correctAnswer", out var ca) && ca.ValueKind != JsonValueKind.Null
                    ? ca.GetString() : null,
                Options: options));
        }

        return result;
    }
}
```

> **Why two JSON parses?** The Anthropic API returns an outer JSON envelope containing Claude's text reply. That reply is itself JSON (we told Claude to return only JSON). First parse extracts the text; second parse extracts the questions.

---

### Step 12 — Create `GenerateFormResponse` DTO

**File to create:** `src/FormAI.Application/Forms/GenerateForm/GenerateFormResponse.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public record GenerateFormResponse(
    Guid FormId,
    string Title,
    SourceType SourceType,
    DateTime CreatedAt,
    IReadOnlyList<GeneratedQuestionResponse> Questions
);

public record GeneratedQuestionResponse(
    Guid QuestionId,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    bool AiGenerated,
    decimal? Points,
    string? CorrectAnswer,
    IReadOnlyList<GeneratedOptionResponse> Options
);

public record GeneratedOptionResponse(
    Guid OptionId,
    string Text,
    int Order,
    bool IsCorrect
);
```

---

### Step 13 — Implement `GenerateFormHandler`

**File to create:** `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs`

```csharp
using FormAI.Application.AI;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.GenerateForm;

public class GenerateFormHandler
{
    private readonly IFormGenerationService _generationService;
    private readonly IFormRepository _forms;

    public GenerateFormHandler(IFormGenerationService generationService, IFormRepository forms)
    {
        _generationService = generationService;
        _forms = forms;
    }

    public async Task<GenerateFormResponse> HandleAsync(
        GenerateFormRequest request,
        Guid requestingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SourceText))
            throw new ArgumentException("Source text is required to generate a form.");

        // Text is always the first source item; files will be added to this list later
        var sourceItems = new List<(SourceType type, string content, string? fileName)>
        {
            (SourceType.Text, request.SourceText, null)
        };

        var combinedText = CombineSources(sourceItems);

        var parameters = new GenerationParameters(
            QuestionCount: request.QuestionCount,
            AllowedTypes: request.AllowedTypes,
            DifficultyLevel: request.DifficultyLevel,
            IncludeCorrectAnswers: request.IncludeCorrectAnswers);

        var generatedQuestions = await _generationService.GenerateAsync(
            combinedText, parameters, cancellationToken);

        var form = Form.Create(
            title: $"Generated Form – {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            description: null,
            createdBy: requestingUserId,
            sourceType: request.SourceType,
            isPublic: false,
            expiresAt: null,
            showResultsAfterSubmit: false);

        var sourceContents = sourceItems.Select((item, i) =>
            FormSourceContent.Create(form.Id, item.type, item.content, order: i + 1, item.fileName))
            .ToList();

        var questions = generatedQuestions.Select((q, i) =>
        {
            var question = FormQuestion.Create(
                formId: form.Id,
                text: q.Text,
                type: q.Type,
                order: i + 1,
                isRequired: q.IsRequired,
                aiGenerated: true,
                points: q.Points,
                correctAnswer: q.CorrectAnswer);

            var options = q.Options.Select((o, oi) =>
                QuestionOption.Create(question.Id, o.Text, order: oi + 1, isCorrect: o.IsCorrect))
                .ToList();

            question.SetOptions(options);
            return question;
        }).ToList();

        form.ReplaceQuestions(questions);
        await _forms.AddAsync(form, sourceContents, cancellationToken);

        return new GenerateFormResponse(
            FormId: form.Id,
            Title: form.Title,
            SourceType: form.SourceType,
            CreatedAt: form.CreatedAt,
            Questions: form.Questions
                .OrderBy(q => q.Order)
                .Select(q => new GeneratedQuestionResponse(
                    QuestionId: q.Id,
                    Text: q.Text,
                    Type: q.Type,
                    Order: q.Order,
                    IsRequired: q.IsRequired,
                    AiGenerated: q.AiGenerated,
                    Points: q.Points,
                    CorrectAnswer: q.CorrectAnswer,
                    Options: q.Options
                        .OrderBy(o => o.Order)
                        .Select(o => new GeneratedOptionResponse(
                            OptionId: o.Id,
                            Text: o.Text,
                            Order: o.Order,
                            IsCorrect: o.IsCorrect ?? false))
                        .ToList()))
                .ToList());
    }

    private static string CombineSources(
        IEnumerable<(SourceType type, string content, string? fileName)> sources)
    {
        var parts = sources.Select((s, i) =>
        {
            var label = s.fileName is not null
                ? $"[Source {i + 1} — {s.type}: {s.fileName}]"
                : $"[Source {i + 1} — {s.type}]";
            return $"{label}\n{s.content}";
        });

        return string.Join("\n\n---\n\n", parts);
    }
}
```

> **`CombineSources`** handles future file sources without any change — when a PDF endpoint is added, its extracted text goes into the same `sourceItems` list and this method combines everything automatically.

---

### Step 14 — Update `IFormRepository` with an overload for source contents

**File:** `src/FormAI.Application/Interfaces/IFormRepository.cs`

Add the overload:
```csharp
Task AddAsync(Form form, IReadOnlyList<FormSourceContent> sourceContents, CancellationToken cancellationToken = default);
```

Add the using at the top:
```csharp
using FormAI.Domain.Entities;
```

---

### Step 15 — Implement the overload in `FormRepository`

**File:** `src/FormAI.Infrastructure/Repositories/FormRepository.cs`

```csharp
public async Task AddAsync(Form form, IReadOnlyList<FormSourceContent> sourceContents, CancellationToken cancellationToken = default)
{
    await _context.Forms.AddAsync(form, cancellationToken);
    await _context.FormSourceContents.AddRangeAsync(sourceContents, cancellationToken);
    await _context.SaveChangesAsync(cancellationToken);
}
```

Both the form and all source content rows are saved in a single `SaveChangesAsync` — atomic: all succeed or none do.

---

### Step 16 — Register `GenerateFormHandler` in DI

**File:** `src/FormAI.Infrastructure/DependencyInjection.cs`

Add alongside the other form handlers:
```csharp
services.AddScoped<GenerateFormHandler>();
```

Add the using at the top:
```csharp
using FormAI.Application.Forms.GenerateForm;
```

---

### Step 17 — Add the endpoint to `FormsController`

**File:** `src/FormAI.API/Controllers/FormsController.cs`

**17a** — Add using:
```csharp
using FormAI.Application.Forms.GenerateForm;
```

**17b** — Add field and inject in constructor:
```csharp
private readonly GenerateFormHandler _generateForm;
```
Add `GenerateFormHandler generateForm` as a constructor parameter and set `_generateForm = generateForm;`.

**17c** — Add the action method:
```csharp
// POST /api/forms/generate/text
[HttpPost("generate/text")]
public async Task<IActionResult> GenerateFromText(
    [FromBody] GenerateFormRequest request,
    CancellationToken cancellationToken)
{
    var response = await _generateForm.HandleAsync(request, CurrentUserId, cancellationToken);
    return CreatedAtAction(nameof(GetById), new { id = response.FormId }, response);
}
```

> `CreatedAtAction` returns HTTP 201 and a `Location` header pointing to `GET /api/forms/{formId}`. Standard REST practice for endpoints that create a resource.

---

## Files Changed Summary

| Action | File |
|---|---|
| Edit | `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs` |
| Edit | `src/FormAI.Application/AI/IFormGenerationService.cs` |
| Edit | `src/FormAI.Domain/Entities/Form.cs` |
| **New** | `src/FormAI.Domain/Entities/FormSourceContent.cs` |
| Edit | `src/FormAI.Infrastructure/Data/Configurations/FormConfiguration.cs` |
| **New** | `src/FormAI.Infrastructure/Data/Configurations/FormSourceContentConfiguration.cs` |
| Edit | `src/FormAI.Infrastructure/Data/AppDbContext.cs` |
| Auto-generated | `src/FormAI.Infrastructure/Migrations/..._AddFormSourceContents.cs` |
| **New** | `src/FormAI.Infrastructure/AI/ClaudeSettings.cs` |
| Edit | `src/FormAI.API/appsettings.json` |
| Edit | `src/FormAI.API/appsettings.Development.json` |
| Edit | `src/FormAI.Infrastructure/DependencyInjection.cs` |
| Edit | `src/FormAI.Infrastructure/AI/ClaudeFormGenerationService.cs` |
| **New** | `src/FormAI.Application/Forms/GenerateForm/GenerateFormResponse.cs` |
| **New** | `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs` |
| Edit | `src/FormAI.Application/Interfaces/IFormRepository.cs` |
| Edit | `src/FormAI.Infrastructure/Repositories/FormRepository.cs` |
| Edit | `src/FormAI.API/Controllers/FormsController.cs` |

---

## Verification

### Build after every step
```bash
dotnet build FormAI.sln
```

### Apply the migration
```bash
dotnet ef database update \
  --project src/FormAI.Infrastructure \
  --startup-project src/FormAI.API
```

### Run the API
```bash
dotnet run --project src/FormAI.API
```
Open Swagger at `http://localhost:5155/swagger`.

### Test 1: Generate from text

1. `POST /api/auth/login` → copy `accessToken`
2. Swagger **Authorize** → `Bearer <token>`
3. `POST /api/forms/generate/text` with body:

```json
{
  "sourceText": "Photosynthesis is the process by which plants use sunlight, water, and carbon dioxide to produce oxygen and energy in the form of glucose. This occurs in chloroplasts. The light-dependent reactions convert solar energy into ATP and NADPH. The Calvin cycle uses that energy to fix carbon dioxide into glucose.",
  "sourceType": 1,
  "sourceUrl": null,
  "questionCount": 3,
  "allowedTypes": null,
  "difficultyLevel": "medium",
  "includeCorrectAnswers": true
}
```

> `"sourceType": 1` = `SourceType.Text` (enum starts at 1, not 0).

**Expected:** HTTP 201 with `formId` and a `questions` array with 3 items. Copy the `formId` for the next tests.

### Test 2: Rename a question
`GET /api/forms/{formId}` → copy the questions → change one `text` field → `PUT /api/forms/{formId}/questions`.
**Expected:** HTTP 204.

### Test 3: Add a manual question
`PUT /api/forms/{formId}/questions` with existing questions plus a new entry with `aiGenerated: false`.
**Expected:** HTTP 204. Subsequent `GET` shows one more question.

### Test 4: Delete a question
`PUT /api/forms/{formId}/questions` omitting one question from the list.
**Expected:** HTTP 204. Subsequent `GET` shows one fewer question.

---

## How File Upload Plugs In Later (Phase 4+)

When you add `POST /api/forms/generate/file`:

1. The endpoint receives `IFormFile` via `[FromForm]`
2. A `IContentExtractor` service (per file type) reads the bytes and returns plain text
3. The extracted text becomes another entry in `sourceItems` in the handler
4. `CombineSources()` combines it with the text source — no other change needed
5. Everything downstream (AI service, question creation, DB save) is unchanged

The `PdfExtractor` and `UrlScraper` stubs already exist in `src/FormAI.Infrastructure/ContentExtraction/` — just implement them and plug their output into `sourceItems`.
