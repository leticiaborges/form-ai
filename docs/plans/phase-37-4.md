# Slice 4 plan: structured output, truncation, error codes

Part of [phase-37](./phase-37.md). Covers AI-5, AI-6 and the generation codes of AI-9.

**Scope.** After this slice a bad model reply answers 502, and a gateway failure or timeout answers 503, each with a `code`. Nothing is retried, since the client never retries (AI-7).

**Decisions made:**
- **The shape of the draft is enforced by the provider, through Structured Outputs** (`response_format` with `type: "json_schema"` and `strict: true`), which works through LiteLLM for both aliases and both providers (checked). The schema is built per request, so `type` is an enum of the allowed types for that call.
- **Two places, two jobs.** The schema can't express string lengths or "options depend on the type" (providers ignore `maxLength`, `minItems` and `maxItems`), so something must still check them. That check is a rule about what a usable question is, so it lives in Application, next to `QuestionOptionValidator`, and not in the gateway adapter:
  - **`GeneratedDraftParser` (Infrastructure)** only reads the reply: the reply is complete (`finish_reason` is not `length` / `content_filter`), has content (a refusal has none), and deserializes into the draft shape. It trims strings and maps to `GeneratedQuestion`. It knows nothing about limits.
  - **`GeneratedDraftValidator` (Application, `Forms/Validation/`)** applies the rules, called by `GenerateFormHandler` right after generation and before the form is built: at least one question, a valid type, non-empty text, strings at most 1024 characters (the database limit; it rejects instead of truncating, as AI-5 says), Single and Multiple have at least 2 options, Text and Numeric have none. Option text goes through the existing `QuestionOptionValidator` (required, at most 1024, unique within the question), so the generated draft and the editor save share one definition.
  - A draft that breaks a rule is a model fault, not the user's, so the validator throws `GenerationException` (`GenerationOutputInvalid`, 502), not the 400 `ValidationException` the editor uses.
  - Trade-off: reusing `QuestionOptionValidator` means a duplicate option now fails the generation (a 502 that still uses a rate-limit permit), where the editor would only have made the owner fix it at save. I kept it because the editor would reject that draft anyway.
- **`ValidateForm` in `GenerateFormHandler` is left alone.** It validates the request (title, source text, description, expiry), not the AI reply, and slices 10 and 14 rewrite its source-text rules. Extracting it into a `GenerateFormRequestValidator` belongs there.
- **Dropped from the earlier version of this plan**, now covered by the schema or not worth a 502: unknown-field checks, question and option maximums, "type was not requested", `isCorrect` type checks, and stripping markdown fences. `max_tokens` already caps size and cost.
- The prompt loses its JSON-structure block, so `PromptVersion` becomes 2.
- The three generation codes are all added now. `GenerationBudgetReached` is only declared. Slice 7 throws it.
- Every non-2xx from the gateway, a timeout, or a connection error becomes `GenerationUnavailable`. The `SourceFileUnreadable` split for provider 4xx on PDFs belongs to slice 15.
- The parser and schema stay `internal` in Infrastructure and are tested through the service in `GatewayFormGenerationServiceTests` (no Docker needed). The validator is public Application code, so its rules are tested in `FormAI.UnitTests`.

Steps 1 to 4 are Application and API, 5 to 8 are Infrastructure, 9 is test support, 10 is tests, and 11 is docs.

---

## 1. Error codes
`src/FormAI.Application/Common/Exceptions/ValidationException.cs`: append to the enum (append only, so existing values keep their meaning).

```csharp
public enum ValidationErrorCode
{
    GenericError,
    FormExpired,
    AlreadySubmitted,
    EmailNotVerified,
    GenerationOutputInvalid,
    GenerationBudgetReached,
    GenerationUnavailable
}
```

## 2. New exception
`src/FormAI.Application/Common/Exceptions/GenerationException.cs` (new file):

```csharp
namespace FormAI.Application.Common.Exceptions;

/// <summary>
/// Generation failed for a reason that is not the caller's fault: a model fault (502)
/// or an unavailable gateway / spent budget (503). The middleware picks the status from <see cref="Code"/>.
/// </summary>
public class GenerationException : Exception
{
    public ValidationErrorCode Code { get; }

    public GenerationException(ValidationErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }
}
```

## 3. Middleware
`src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs`. Add the arm after `ValidationException`:

```csharp
            ValidationException ex => (HttpStatusCode.BadRequest, ex.Message, (object?)ex.Errors, ex.Code),
            GenerationException ex => (
                ex.Code == ValidationErrorCode.GenerationOutputInvalid ? HttpStatusCode.BadGateway : HttpStatusCode.ServiceUnavailable,
                ex.Message, (object?)null, (ValidationErrorCode?)ex.Code),
            UnauthorizedAccessException ex => ...
```

Then log it, since only 500s are logged today and the inner exception is the only diagnostic:

```csharp
        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Unhandled exception");
        else if (exception is GenerationException)
            _logger.LogWarning(exception, "Generation failed with {Code}", code);
```

## 4. Draft validator
`src/FormAI.Application/Forms/Validation/GeneratedDraftValidator.cs` (new file). It collects every problem, like the other validators, and throws once.

```csharp
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.Validation;

public static class GeneratedDraftValidator
{
    public const int MaxTextLength = 1024;

    /// <summary>
    /// Applies the rules a generated draft must meet to become a form: at least one question, each with
    /// a valid type and text, strings within the database limit, and options that match the type.
    /// A draft that breaks them is a model fault, not the user's, so this throws
    /// <see cref="GenerationException"/> (502) and never the 400 <see cref="ValidationException"/>.
    /// </summary>
    public static void Validate(IReadOnlyList<GeneratedQuestion> questions)
    {
        var problems = new List<string>();

        if (questions.Count == 0)
            problems.Add("The draft has no questions.");

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            var where = $"Question {i + 1}";

            if (string.IsNullOrWhiteSpace(q.Text))
                problems.Add($"{where}: text is required.");
            else if (q.Text.Length > MaxTextLength)
                problems.Add($"{where}: text is over {MaxTextLength} characters.");

            if (q.CorrectAnswer?.Length > MaxTextLength)
                problems.Add($"{where}: correctAnswer is over {MaxTextLength} characters.");

            if (!Enum.IsDefined(q.Type))
            {
                problems.Add($"{where}: type is not valid.");
                continue;
            }

            var needsOptions = q.Type is QuestionType.Single or QuestionType.Multiple;
            if (needsOptions && q.Options.Count < 2)
                problems.Add($"{where}: needs at least 2 options.");
            if (!needsOptions && q.Options.Count != 0)
                problems.Add($"{where}: must have no options.");

            // Same option rules as the editor: required, at most 1024, unique within the question.
            var optionErrors = new Dictionary<string, string[]>();
            QuestionOptionValidator.Validate(i, q.Options.Select(o => o.Text).ToList(), optionErrors);
            problems.AddRange(optionErrors.Values.SelectMany(m => m).Select(m => $"{where}: {m}"));
        }

        if (problems.Count > 0)
            // The message is what the user sees; the detail goes on the inner exception for the log.
            throw new GenerationException(
                ValidationErrorCode.GenerationOutputInvalid,
                "The AI returned a draft we could not use. Please try again.",
                new InvalidOperationException(string.Join(" ", problems)));
    }
}
```

`src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs`: call it right after generation, before anything is built or saved.

```csharp
        var generatedQuestions = await _generationService.GenerateAsync(request.SourceText, parameters, requestingUserId, cancellationToken);

        GeneratedDraftValidator.Validate(generatedQuestions);
```

Add `using FormAI.Application.Forms.Validation;` at the top.

## 5. Response schema
`src/FormAI.Infrastructure/AI/DraftSchema.cs` (new file). Strict mode needs every property in `required` and `additionalProperties: false`, with optional values typed as `["string", "null"]`.

```csharp
using FormAI.Application.AI;
using FormAI.Domain.Enums;

namespace FormAI.Infrastructure.AI;

/// <summary>
/// The Structured Outputs schema sent as <c>response_format</c>. It fixes the shape of the draft and the
/// allowed question types; what it can't express (string lengths, option counts) is checked by
/// <c>GeneratedDraftValidator</c> in Application. Keep the two in sync.
/// </summary>
internal static class DraftSchema
{
    public static object ResponseFormat(GenerationParameters parameters)
    {
        var types = (parameters.AllowedTypes is { Length: > 0 } ? parameters.AllowedTypes : Enum.GetValues<QuestionType>())
            .Select(t => t.ToString())
            .ToArray();

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "form_draft",
                strict = true,
                schema = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "questions" },
                    properties = new
                    {
                        questions = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                additionalProperties = false,
                                required = new[] { "text", "type", "correctAnswer", "options" },
                                properties = new
                                {
                                    text = new { type = "string" },
                                    type = new { type = "string", @enum = types },
                                    correctAnswer = new { type = new[] { "string", "null" } },
                                    options = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            additionalProperties = false,
                                            required = new[] { "text", "isCorrect" },
                                            properties = new
                                            {
                                                text = new { type = "string" },
                                                isCorrect = new { type = new[] { "boolean", "null" } }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
```

## 6. Parser
`src/FormAI.Infrastructure/AI/GeneratedDraftParser.cs` (new file). It only reads the reply and maps it; the rules are in `GeneratedDraftValidator` (step 4).

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Enums;

namespace FormAI.Infrastructure.AI;

/// <summary>
/// Reads the gateway's OpenAI-compatible reply into <see cref="GeneratedQuestion"/>s. The provider already
/// enforces the draft's shape (<see cref="DraftSchema"/>); this only checks that the reply is complete and
/// readable. Whether the draft is usable (limits, option counts) is <c>GeneratedDraftValidator</c>'s job.
/// A reply that can't be read throws a GenerationOutputInvalid <see cref="GenerationException"/>.
/// </summary>
internal static class GeneratedDraftParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private sealed record Draft(List<DraftQuestion>? Questions);
    private sealed record DraftQuestion(string? Text, QuestionType Type, string? CorrectAnswer, List<DraftOption>? Options);
    private sealed record DraftOption(string? Text, bool? IsCorrect);

    public static IReadOnlyList<GeneratedQuestion> Parse(string responseJson)
    {
        try
        {
            return ParseCore(responseJson);
        }
        catch (JsonException ex)
        {
            throw Invalid("The AI reply was not valid JSON.", ex);
        }
    }

    private static IReadOnlyList<GeneratedQuestion> ParseCore(string responseJson)
    {
        using var envelope = JsonDocument.Parse(responseJson);

        if (!envelope.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw Invalid("The gateway reply had no choices.");

        var choice = choices[0];

        // A cut-off draft is a model fault, never retried (AI-6).
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String
            && (finish.GetString() is "length" or "content_filter"))
            throw Invalid($"The AI reply stopped early ({finish.GetString()}).");

        // A refusal arrives with no content.
        if (!choice.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            throw Invalid("The gateway reply had no content.");

        var draft = JsonSerializer.Deserialize<Draft>(content.GetString()!, Json);

        if (draft?.Questions is not { } questions)
            throw Invalid("The draft had no questions array.");

        return questions.Select(ToQuestion).ToList();
    }

    // Trims only; a missing string becomes empty so the validator reports it. A missing type deserializes
    // to 0, which the validator rejects as not a QuestionType.
    private static GeneratedQuestion ToQuestion(DraftQuestion q) => new(
        Text: q.Text?.Trim() ?? string.Empty,
        Type: q.Type,
        IsRequired: true,
        CorrectAnswer: string.IsNullOrWhiteSpace(q.CorrectAnswer) ? null : q.CorrectAnswer.Trim(),
        Options: (q.Options ?? [])
            .Select(o => new GeneratedOption(o.Text?.Trim() ?? string.Empty, o.IsCorrect))
            .ToList());

    // The message is what the user sees, so it stays generic; the detail goes on the inner exception for the log.
    private static GenerationException Invalid(string detail, Exception? inner = null) =>
        new(ValidationErrorCode.GenerationOutputInvalid,
            "The AI returned a draft we could not use. Please try again.",
            inner ?? new InvalidOperationException(detail));
}
```

## 7. Service
`src/FormAI.Infrastructure/AI/GatewayFormGenerationService.cs`:

- Add `using FormAI.Application.Common.Exceptions;` and remove the now-unused `System.Text.RegularExpressions` and `Humanizer` usings.
- Change `public const int PromptVersion = 1;` to `2`.
- Add `response_format` to the request object, after `user`:

```csharp
            user = userId.ToString(),
            response_format = DraftSchema.ResponseFormat(parameters),
            messages = new object[]
```

- Replace the send and parse section of `GenerateAsync` (the `using var response = ...` through `return ParseResponse(...)`) with:

```csharp
        string responseJson;
        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw Unavailable(new HttpRequestException($"The gateway answered {(int)response.StatusCode}."));

            responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw Unavailable(ex);
        }
        // HttpClient reports its own timeout as a cancellation; the caller's cancellation still propagates.
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable(ex);
        }

        return GeneratedDraftParser.Parse(responseJson);
    }

    private static GenerationException Unavailable(Exception inner) =>
        new(ValidationErrorCode.GenerationUnavailable,
            "Form generation is unavailable right now. Please try again later.", inner);
```

- Delete the old `ParseResponse` method.

## 8. Prompt
`src/FormAI.Infrastructure/AI/Prompt/PromptGenerateForm.txt`. The structure block and "Return ONLY a valid JSON object" go, since the schema carries them. Replace the whole file with:

```
You are a quiz/form generator. Given source text, generate exactly {questionCount} questions.
Follow below definition to check wich types of question should be generated, with which
difficultyLevel and whether or not to mark answers as correct (in case you don't need to mark as correct you can return isCorrect as null and don't give an correctAnswer).
Even though you will receive a text or file with content, the questions should not reference the content, because the content won't be visible to the user.

Allowed question types: {allowedTypes}.
Difficulty: {difficultyLevel}.
Should mark correct answers: {markCorrect}

Rules:
- "Single" and "Multiple" questions MUST have at least 2 options.
- "Text" and "Numeric" questions MUST have an empty options array: [].
- "Mark correct option(s) with isCorrect: true." only if 'Should mark correct answers: true', otherwise leave as null.
- For Numeric questions, correctAnswer should be the number as a string (e.g. "42"), filled with the correct answer. Should only be if 'Should mark correct answers: true'.
- For Text questions, correctAnswer is a sample/expected answer string with the right response. Should only be filled if 'Should mark correct answers: true'.
- Max characters of the question is 1024
- Max characters for each option is 1024
- Max characters for text answer is 1024
```

The existing test asserts the system prompt contains `exactly 2 questions`. That line is unchanged.

## 9. Fake gateway: one case per code (T-1)
`frontend/e2e/support/fake-gateway.mjs`. Replace the `req.resume(); req.on("end", ...)` block. The scenario is chosen by a marker in the pasted text, so an e2e test can pick it without extra config.

```js
    const chunks = [];
    req.on("data", (c) => chunks.push(c));

    req.on("end", () => {
      const body = Buffer.concat(chunks).toString("utf8");

      // Scenarios are picked by a marker in the pasted source text.
      if (body.includes("[fake:down]")) return send(res, 503, { error: { message: "Gateway down" } });
      if (body.includes("[fake:slow]")) return; // never answers; the client timeout fires

      const reply = (finish_reason, content) =>
        send(res, 200, {
          id: "chatcmpl-fake",
          object: "chat.completion",
          choices: [{ index: 0, finish_reason, message: { role: "assistant", content } }],
          usage: { prompt_tokens: 10, completion_tokens: 10, total_tokens: 20 },
        });

      if (body.includes("[fake:truncated]")) return reply("length", JSON.stringify(draft).slice(0, 60));
      if (body.includes("[fake:invalid]")) return reply("stop", JSON.stringify({ questions: [] }));

      reply("stop", JSON.stringify(draft));
    });
```

## 10. Tests

### 10a. Draft rules (unit tests, no Docker)
`tests/FormAI.UnitTests/Forms/GeneratedDraftValidatorTests.cs` (new file):

```csharp
using FormAI.Application.AI;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Domain.Enums;

namespace FormAI.UnitTests.Forms;

public class GeneratedDraftValidatorTests
{
    private static GeneratedQuestion Q(string text = "Q?", QuestionType type = QuestionType.Text,
        string? correctAnswer = null, params string[] options) =>
        new(text, type, IsRequired: true, correctAnswer, options.Select(o => new GeneratedOption(o, null)).ToList());

    private static void AssertInvalid(params GeneratedQuestion[] questions)
    {
        var ex = Assert.Throws<GenerationException>(() => GeneratedDraftValidator.Validate(questions));
        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
    }

    [Fact]
    public void ADraftWithOneQuestionOfEachType_Passes()
    {
        GeneratedDraftValidator.Validate([
            Q("S?", QuestionType.Single, null, "A", "B"),
            Q("M?", QuestionType.Multiple, null, "A", "B", "C"),
            Q("T?", QuestionType.Text, "Because"),
            Q("N?", QuestionType.Numeric, "42")]);
    }

    [Fact]
    public void ADraftWithNoQuestions_IsInvalid() => AssertInvalid();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void QuestionTextIsRequired(string text) => AssertInvalid(Q(text));

    [Fact]
    public void QuestionTextOver1024Characters_IsInvalid() => AssertInvalid(Q(new string('x', 1025)));

    [Fact]
    public void QuestionTextOf1024Characters_Passes() =>
        GeneratedDraftValidator.Validate([Q(new string('x', 1024))]);

    [Fact]
    public void ACorrectAnswerOver1024Characters_IsInvalid() => AssertInvalid(Q(correctAnswer: new string('x', 1025)));

    [Fact]
    public void AnUndefinedType_IsInvalid() => AssertInvalid(Q(type: (QuestionType)0));

    [Fact]
    public void ASingleQuestionWithOneOption_IsInvalid() => AssertInvalid(Q(type: QuestionType.Single, options: "Only"));

    [Fact]
    public void ATextQuestionWithOptions_IsInvalid() => AssertInvalid(Q(type: QuestionType.Text, options: ["A", "B"]));

    [Fact]
    public void AnOptionWithNoText_IsInvalid() => AssertInvalid(Q(type: QuestionType.Single, options: ["A", " "]));

    [Fact]
    public void AnOptionOver1024Characters_IsInvalid() =>
        AssertInvalid(Q(type: QuestionType.Single, options: ["A", new string('x', 1025)]));

    [Fact]
    public void DuplicateOptions_AreInvalid_LikeInTheEditor() =>
        AssertInvalid(Q(type: QuestionType.Single, options: ["A", " a "]));
}
```

`tests/FormAI.UnitTests/Forms/GenerateFormTests.cs`: the handler must stop before saving anything.

```csharp
    [Fact]
    public async Task AnUnusableDraft_ThrowsGenerationOutputInvalid_AndSavesNothing()
    {
        SetGenerationServiceQuestions([new GeneratedQuestion("", QuestionType.Text, true, null, [])]);

        var ex = await Assert.ThrowsAsync<GenerationException>(() =>
            _handler.HandleAsync(CreateRequest(), Guid.NewGuid()));

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
        await _forms.DidNotReceive().AddAsync(Arg.Any<Form>(), Arg.Any<CancellationToken>());
    }
```

The existing drafts in this file are valid (unique options, empty options for Text and Numeric), so the other tests in it should still pass. If one doesn't, its draft was breaking a rule.

### 10b. Gateway client (integration project, no Docker)
`tests/FormAI.IntegrationTests/GatewayFormGenerationServiceTests.cs`.

First, add `using FormAI.Application.Common.Exceptions;` and change `Envelope` to carry a finish reason (existing tests keep working):

```csharp
    private static string Envelope(string content, string finishReason = "stop") =>
        JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = finishReason, message = new { content } } } });
```

Then add inside the class:

```csharp
    private static string DraftOf(string question) => $$"""{"questions":[{{question}}]}""";

    // Only what the parser itself can't read. Whether a readable draft is usable is GeneratedDraftValidatorTests.
    public static TheoryData<string> UnreadableDrafts => new()
    {
        "not json at all",
        """{}""",
        DraftOf("""{"text":"Q","type":"Bogus","correctAnswer":null,"options":[]}"""),
    };

    private async Task<GenerationException> Fails(StubHandler handler) =>
        await Assert.ThrowsAsync<GenerationException>(() =>
            CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid()));

    [Fact]
    public async Task SendsAStrictJsonSchemaLimitedToTheAllowedTypes()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
        var parameters = new GenerationParameters(2, [QuestionType.Single, QuestionType.Text]);

        await CreateService(handler).GenerateAsync("text", parameters, Guid.NewGuid());

        var format = handler.Body!.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());

        var jsonSchema = format.GetProperty("json_schema");
        Assert.True(jsonSchema.GetProperty("strict").GetBoolean());

        var type = jsonSchema.GetProperty("schema").GetProperty("properties").GetProperty("questions")
            .GetProperty("items").GetProperty("properties").GetProperty("type");
        Assert.Equal(["Single", "Text"], type.GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
    }

    [Theory]
    [MemberData(nameof(UnreadableDrafts))]
    public async Task AnUnreadableDraft_ThrowsGenerationOutputInvalid(string draft)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(draft))));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
        Assert.Equal(1, handler.Calls); // never retried
    }

    [Fact]
    public async Task ARefusalWithNoContent_ThrowsGenerationOutputInvalid()
    {
        var refusal = JsonSerializer.Serialize(new
        {
            choices = new[] { new { finish_reason = "stop", message = new { content = (string?)null, refusal = "No." } } }
        });
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, refusal)));

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, (await Fails(handler)).Code);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    public async Task ACutOffReply_ThrowsGenerationOutputInvalid_AndIsNotRetried(string finishReason)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft, finishReason))));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationOutputInvalid, ex.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task ANonSuccessGatewayReply_ThrowsGenerationUnavailable(HttpStatusCode status)
    {
        var handler = new StubHandler(() => Task.FromResult(Json(status, """{"error":"x"}""")));

        var ex = await Fails(handler);

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, ex.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AConnectionFailure_ThrowsGenerationUnavailable()
    {
        var handler = new StubHandler(() => Task.FromException<HttpResponseMessage>(new HttpRequestException("refused")));

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, (await Fails(handler)).Code);
    }

    [Fact]
    public async Task AnHttpClientTimeout_ThrowsGenerationUnavailable()
    {
        // HttpClient surfaces its own timeout as a TaskCanceledException while the caller's token is untouched.
        var handler = new StubHandler(() => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")));

        Assert.Equal(ValidationErrorCode.GenerationUnavailable, (await Fails(handler)).Code);
    }

    [Fact]
    public async Task ACallerCancellation_StillPropagatesAsCancellation()
    {
        var handler = new StubHandler(() => Task.FromResult(Json(HttpStatusCode.OK, Envelope(Draft))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(handler).GenerateAsync("text", Parameters, Guid.NewGuid(), cts.Token));
    }
```

The existing `Draft` constant omits `correctAnswer` on some questions and always includes it on others. The parser reads a missing `correctAnswer` as null, so those tests still pass.

## 11. Docs (same change)
**`CLAUDE.md`**, in the Architecture paragraph on `ExceptionHandlingMiddleware`:
- Add `GenerationException` → 502 for `GenerationOutputInvalid`, 503 for `GenerationUnavailable` and `GenerationBudgetReached`.
- Add `ValidationErrorCode` entries `GenerationOutputInvalid`, `GenerationUnavailable` and `GenerationBudgetReached` (the last is declared, not yet thrown).

In the "AI integration" section, add: the request carries a strict JSON schema (`response_format`, built per request in `DraftSchema`), so the provider enforces the draft's shape. `GeneratedDraftParser` only checks that the reply is complete and readable. `GeneratedDraftValidator` (Application, called by `GenerateFormHandler` before the form is built) applies the rules a schema can't express: the 1024 limit, option counts per type and the editor's option rules. A length cut-off, a refusal or an unusable draft is `GenerationOutputInvalid`, and a gateway error or timeout is `GenerationUnavailable`. The client never retries. Change "prompt version" mentions to 2 if any exist.

**`docs/known-gaps.md`**: add "`GenerationBudgetReached` is declared but nothing throws it until the ceiling ships (slice 7)."

## 12. Verify
```powershell
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
dotnet test tests/FormAI.IntegrationTests/FormAI.IntegrationTests.csproj --filter GatewayFormGenerationServiceTests
```

For a manual check against the real gateway, generate once for a text with each alias in use (`form-generator`) and confirm the draft comes back without fences. For the error paths, start the fake gateway (`node frontend/e2e/support/fake-gateway.mjs`) and point `Ai:GatewayUrl` and `Ai:ApiKey` at it. Then POST to `/api/forms/generate/text` with `[fake:truncated]`, `[fake:invalid]`, `[fake:down]` in the source text. The bodies should show 502 `GenerationOutputInvalid`, 502 `GenerationOutputInvalid` and 503 `GenerationUnavailable`. `[fake:slow]` answers 503 only after `Ai:TimeoutSeconds` (80 s by default).

Suggested commit: `feat(ai): request a strict JSON schema and map generation failures to 502/503`.
