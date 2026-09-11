# Phase 29.1: Expired-Form Error — Alternative via a Plain-Message `ValidationException` + Stable Error Codes

**This is an alternative to `phase-29.md`, not an addition to it.** Both solve the same problem — the answer page showing the real "this form has expired" message instead of falling into the generic error state. They differ in *where* the fix lives: `phase-29` teaches the frontend to dig the message out of `errors["form"]`; this document changes `ValidationException` itself so the top-level `message` is *always* the real, specific text when there's no field to attach it to, and reserves `errors` for genuine field-by-field breakdowns. Read both, pick one, discard the other's frontend changes (the server-side `IsExpired`-removal step is common to both).

## The pattern this follows

Surveyed enterprise APIs (RFC 9457/ASP.NET's `ValidationProblemDetails`, Stripe, Azure, Google Cloud, JSON:API, GitHub) split errors along one consistent line: a **single, always-meaningful top-level message**, plus an **optional field-level breakdown** that's populated only when the problem is actually field-shaped. None of them make the top-level message a permanent placeholder — it's generic *only* when there genuinely are multiple field problems to summarize; for a single whole-request reason, the top-level message *is* the reason.

Today, `ValidationException.Message` is hardcoded to `"One or more validation errors occurred."` no matter what — even when there's exactly one problem and it isn't field-shaped at all (a form being expired isn't a bad `expiresAt` field on the *request*, it's a state fact about the resource). The real text only exists inside `Errors["form"]`, so anything that wants it has to know to look there.

**Revision note:** the first draft of this document had the frontend detect the expired case by checking `status === 400` on the response, on the (correct, but implicit) assumption that `GetFormToAnswerHandler` only ever throws one kind of 400. That's fragile — nothing stops a future validation rule from being added to the same endpoint, at which point the frontend would silently mislabel an unrelated 400 as "expired." Matching on the message *text* would be worse for the same reason: it's documented, everywhere from Stripe to RFC 9457, as free-form and human-facing, never a contract a client should parse. The fix, same as every surveyed API uses (Stripe's `type`/`code`, Azure's `error.code`, Google's `status` enum, GitHub's per-error `code`): every plain-message `ValidationException` now also carries a **stable, machine-readable error code**, separate from both the prose and the HTTP status. The client switches on that; the message stays free to reword.

## The rule for which constructor to use

Not every current dictionary-based throw should convert — only the ones that are thrown **alone, as the sole reason for rejection**, where nothing else could simultaneously be wrong. Two examples already in the codebase clarify the line:

- `FormAccessValidator.CheckUserAnswerAccess`'s expiry check, and `SubmitFormHandler`'s "you already submitted this form" check (`SubmitFormHandler.cs:76-80`) — both throw immediately and alone. **These convert**, and each gets its own error code.
- `SubmitFormHandler`'s `errors["answers"] = [...]` (`SubmitFormHandler.cs:86-87`) — this is added to a dictionary that's built up across multiple checks and can be thrown *together* with other question-keyed entries in the same response (`ValidateFormAsync` accumulates into `errors`, `ValidateAnswers` adds more question-id keys to the same dictionary, and `HandleAsync` throws once at the end). **This stays a dictionary entry** — collapsing it into a top-level message would mean it silently wins over, or gets silently dropped alongside, any other field errors collected in the same request. It also needs no error code: a field error is already discriminated by its own key, which is exactly what a code would otherwise be for.

Rule of thumb: if the throw site could only ever be reporting *this one thing* → plain message + code. If the value it writes is merged into a shared `errors` dictionary alongside other possible entries → keep it a dictionary entry, no code.

## Server-side changes

### 1. Add an error-code enum and a plain-message constructor to `ValidationException`

**`src/FormAI.Application/Common/Exceptions/ValidationException.cs`**

```csharp
namespace FormAI.Application.Common.Exceptions;

public enum ValidationErrorCode
{
    FormExpired,
    AlreadySubmitted
}

public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public ValidationErrorCode? Code { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }

    public ValidationException(ValidationErrorCode code, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]>();
        Code = code;
    }
}
```

`ValidationErrorCode` is a real C# enum, not a raw string, deliberately: this codebase already represents `QuestionType`/`SourceType` as enums serialized to strings (see `Program.cs:15-18`'s `JsonStringEnumConverter`), so it's the idiomatic choice here, not a new convention — and it buys compile-time safety a string literal wouldn't (a typo'd enum member fails to compile; a typo'd string doesn't fail until a client silently doesn't match it). It also puts the entire known vocabulary of single-reason errors in one place instead of scattering string literals across throw sites. Add to this enum only when a new *standalone* (see the rule above) validation error needs to be distinguishable by the client — not for every possible message.

**Design decision — `Errors` is always a non-null, possibly-empty dictionary, never `null`.** This is deliberate: every consumer (server-side and any frontend field-error reader) can keep treating `Errors`/`errors` as always-safe-to-index without a null check, exactly like today. The tradeoff against literally mirroring RFC 9457 (which *omits* the `errors` extension member entirely when there's nothing to put in it) is a few harmless bytes of `"errors": {}` in every plain-message response, in exchange for one less nullable case everywhere else in the codebase. Same reasoning applies to `Code` being nullable instead of split into a second exception type — a `ValidationException` from the dictionary constructor simply has `Code == null`, which the middleware and the frontend both treat as "no code, this is a field-errors response."

### 2. Wire `Code` through `ExceptionHandlingMiddleware`

**`src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs`**

Two things need to change here, and both matter — this was checked against the actual current code, not assumed:

1. The switch expression needs a fourth element (`code`) in every branch, `null` except for `ValidationException`.
2. The middleware builds its error JSON with its **own** `new JsonSerializerOptions { PropertyNamingPolicy = CamelCase }` (line 53-58) — a separate instance from the one `Program.cs:15-18` configures for MVC controller responses, which is where `JsonStringEnumConverter` currently lives. The middleware's options object does **not** include that converter today. Left as-is, `Code` would serialize as a raw integer (`0`, `1`, ...) instead of a string like every other enum this API returns (`"Single"`, `"Text"`, ...) — inconsistent, and a trap for whoever reads the response next. Add the converter here too:

```csharp
private async Task HandleExceptionAsync(HttpContext context, Exception exception)
{
    var (statusCode, message, errors, code) = exception switch
    {
        NotFoundException ex => (HttpStatusCode.NotFound, ex.Message, (object?)null, (ValidationErrorCode?)null),
        ForbiddenException ex => (HttpStatusCode.Forbidden, ex.Message, (object?)null, (ValidationErrorCode?)null),
        ValidationException ex => (HttpStatusCode.BadRequest, ex.Message, (object?)ex.Errors, ex.Code),
        UnauthorizedAccessException ex => (HttpStatusCode.Unauthorized, ex.Message, (object?)null, (ValidationErrorCode?)null),
        _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.", (object?)null, (ValidationErrorCode?)null)
    };

    if (statusCode == HttpStatusCode.InternalServerError)
        _logger.LogError(exception, "Unhandled exception");

    context.Response.ContentType = "application/json";
    context.Response.StatusCode = (int)statusCode;

    var body = JsonSerializer.Serialize(new { message, errors, code },
        new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        });

    await context.Response.WriteAsync(body, context.RequestAborted);
}
```

Resulting body for the expiry case: `{ "message": "This form is no longer accepting submissions.", "errors": {}, "code": "FormExpired" }`. For every other exception type, `code` is simply `null` and the frontend ignores it, same as it already ignores `errors` on a `NotFoundException` today.

### 3. Simplify `FormAccessValidator.CheckUserAnswerAccess`

**`src/FormAI.Application/Forms/Validation/FormAccessValidator.cs`**

```csharp
public static void CheckUserAnswerAccess(Form? form, Guid? requestingUserId)
{
    if (form is null)
        throw new NotFoundException("Form not found.");

    if (!form.IsPublic)
        throw new NotFoundException("You don't have access to this form.");

    if (form.IsExpired)
        throw new ValidationException(ValidationErrorCode.FormExpired,
            "This form is no longer accepting submissions.");
}
```

### 4. Convert the other standalone throw in `SubmitFormHandler`

**`src/FormAI.Application/Submissions/SubmitForm/SubmitFormHandler.cs:76-80`**

```csharp
if (existing is not null)
    throw new ValidationException(ValidationErrorCode.AlreadySubmitted,
        "You have already submitted this form.");
```

(replacing the current `new ValidationException(new Dictionary<string, string[]> { ["form"] = new[] { "..." } })`). Leave the `errors["answers"] = [...]` block (`SubmitFormHandler.cs:86-87`) exactly as it is — per the rule above, it can share a response with other question-keyed errors and doesn't need a code.

### 5. Drop the now-unreachable `IsExpired` field from the answer response

Same as `phase-29.md` step 1 — unrelated to which `ValidationException` approach you pick, this field can never be `true` in a response that actually reaches the client either way.

**`src/FormAI.Application/Submissions/GetFormToAnswer/GetFormToAnswerRequest.cs`**
```csharp
public record GetFormToAnswerResponse(
    Guid Id,
    string Title,
    string? Description,
    List<AnswerQuestionDTO> Questions
);
```

**`src/FormAI.Application/Submissions/GetFormToAnswer/GetFormToAnswerHandler.cs`**
```csharp
return new GetFormToAnswerResponse(form.Id, form.Title, form.Description, questions);
```

## Client-side changes

This is where the two approaches diverge most — this path needs **less** frontend code than `phase-29.md`, because `data.message` is now correct by construction; nothing has to dig into `data.errors` for this specific case, and the discrimination between "expired" and "any other error" now rests on a stable field instead of status code or message text.

### 6. Add `code` to the shared error response type

**`frontend/src/types/CustomResponse.ts`**

```ts
export interface CustomResponse {
    response?: DataResponse;
}

export interface DataResponse {
    data?: MessageErrorResponse
}

export interface MessageErrorResponse {
    message?: string;
    errors?: Record<string, string[]>;
    code?: string;
}
```

### 7. Remove the dead `isExpired` field from the frontend type

**`frontend/src/types/submission.ts`** — same as `phase-29.md` step 3:
```ts
export interface AnswerForm {
    id: string;
    title: string;
    description: string | null;
    questions: AnswerQuestion[]
}
```

### 8. Route the `FormExpired` code to the `'expired'` state

**`frontend/src/pages/FormAnswerPage.tsx`**

No new helper file needed — `getErrorMessage` already reads `data.message`, and that's now the real text. Add state to hold it, same as `phase-29.md`, plus import `CustomResponse`:

```ts
import type { CustomResponse } from "../types/CustomResponse";
```

```ts
const [expiredMessage, setExpiredMessage] = useState<string | null>(null);
```

```ts
getMySubmission(id, respondentToken)
    .then(sub => {
        if (sub.hasSubmitted) {
            setState('alreadySubmitted');
            return;
        }

        return getFormToAnswer(id).then(data => {
            setForm(data);
            setState('ready');
        });
    })
    .catch(err => {
        const e = err as CustomResponse;
        if (e.response?.data?.code === 'FormExpired') {
            setExpiredMessage(getErrorMessage(err, "This form is no longer accepting submissions."));
            setState('expired');
            return;
        }
        setState('error');
    });
```

This is the actual fix for the fragility flagged above: it no longer matters whether `GetFormToAnswerHandler` is the *only* thing that can produce a 400 on this endpoint — the check is explicit about *which* 400 it's reacting to, and stays correct even if the endpoint grows other validation rules later that should fall through to the generic `'error'` state instead.

```ts
case 'expired':
    return expiredMessage ?? "This form is no longer accepting submissions.";
```

### 9. The submit-time toast needs no change at all

`phase-29.md`'s optional step 6 (fixing `handleSubmit`'s catch to show the specific expiry/already-submitted text instead of the generic boilerplate) **is no longer a separate task** — `getErrorMessage(err, 'Failed to submit. Please try again.')` already reads `data.message`, which is now correct automatically once steps 1–4 above land. This is the main practical win of this approach over `phase-29.md`: the fix happens once, at the exception, instead of needing a new frontend helper (`getFieldError`) plus remembering every call site that should use it.

## Comparison at a glance

| | `phase-29.md` | `phase-29.1.md` (this doc) |
|---|---|---|
| Where the fix lives | Frontend learns to read `errors["form"]` | Backend makes `message` correct by construction, plus a stable `code` |
| Discrimination signal | Presence of `errors["form"]` | `code === 'FormExpired'` |
| New frontend helper | `getFieldError` in `getErrorMessage.ts` | None |
| Submit-toast fix | Separate optional step, easy to forget at other call sites | Free — fixed at the source |
| `ValidationException` shape | Unchanged (dictionary-only) | New plain-message + code constructor added |
| Middleware changes | None | Adds `code` to the response, and a `JsonStringEnumConverter` to its local serializer options |
| Risk | Every future "single reason" error still needs a `["form"]`-style pseudo-key, and callers must remember to check `errors` instead of `message` | Two constructors on one type, plus a growing enum — must keep following the "alone vs. collected" rule above, or the split gets muddled over time |
| Matches this codebase's existing convention | Yes — no changes to a shared type | Partially — first behavioral change to `ValidationException`, but the enum-as-string pattern matches `QuestionType`/`SourceType` |

## Not in scope here

- Whether "form expired" / "already submitted" should really be a `409 Conflict` (or `410 Gone`) instead of `400 Bad Request` — several of the surveyed APIs give state-conflict problems a different status than field validation, only the message and code ever change here. Worth a separate conversation; changing it would mean a new exception type and a new middleware branch, which is a bigger change than this document's scope.
- The `CheckUserAnswerAccess` `NotFoundException` message-parity issue (`"Form not found."` vs `"You don't have access to this form."`) — same note as in `phase-29.md`, unrelated to the expired-message problem. (Could eventually get its own codes too — `FormNotFound` / `FormNotAccessible` — but that's a 404, and nothing today needs to discriminate between those two 404 reasons client-side, so it's not pulled into this pass.)
- `docs/known-gaps.md` / CLAUDE.md updates for the `CheckUserAnswerAccess` ownership rule change — same note as in `phase-29.md`.

## Manual test checklist

Same as `phase-29.md`, plus one new check for the code itself:

1. Publish a form, force its expiry into the past, visit `/forms/{id}/answer` as a fresh respondent → expired message shown, sourced from the backend, and the network response body shows `"code": "FormExpired"`.
2. Non-expired public form → still shows the question list.
3. Private or nonexistent form id → still falls into `'error'` (response has no `code`).
4. Submit after the form expires (or after already having submitted) → toast shows the specific reason, not "One or more validation errors occurred."
5. Confirm in the browser network tab that `code` is a string (`"FormExpired"`), not a number — this is the exact regression the `JsonStringEnumConverter` step above prevents.
