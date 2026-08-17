# Phase 11: Public form-response flow (answer & submit)

## Context

Today there is no way for anyone other than the form owner to view a form's
questions and answer them. The data model already anticipates this — `CLAUDE.md`
describes `Submission`/`Answer`/`AnswerSelectedOption` entities, a
`respondentToken` (UUID from localStorage) for anonymous respondents, and
business rules like "expired forms reject new submissions" and "answer key
hidden until closed" — but almost none of that is actually wired up yet:

- `Submission`/`Answer`/`AnswerSelectedOption` domain entities, EF configs, and
  migrations already exist (`src/FormAI.Domain/Entities/{Submission,Answer,AnswerSelectedOption}.cs`),
  so the tables are there — but there's no `ISubmissionRepository` at all, and
  `src/FormAI.API/Controllers/SubmissionsController.cs` is an empty stub (just
  route comments, no code).
- `src/FormAI.Application/Submissions/SubmitForm/SubmitFormRequest.cs` defines
  the request/response shape, but **`SubmitFormHandler` doesn't exist** — no
  validation, no scoring, no persistence logic anywhere.
- The only way to fetch a form today is `GetFormHandler` (used by the editor),
  and it **leaks the answer key**: `GetFormResponse` includes `CorrectAnswer`
  on every question and `IsCorrect` on every option, unconditionally
  (`src/FormAI.Application/Forms/GetForm/GetFormHandler.cs`,
  `GetFormResponse.cs`). Reusing it for a public respondent page would hand
  out the correct answers before they submit.
- Neither `Form.IsExpired` nor duplicate-submission checks are enforced
  anywhere in application code today (duplicates only fail via a raw DB
  unique-constraint violation, which isn't mapped to a friendly error).
- On the frontend, there is no respond-facing route, page, API client, or
  types for submissions — only the owner-facing editor (`FormEditorPage.tsx`,
  which must **not** be reused for this, per the requirement that respondents
  can't touch title/description/questions/options/order).

This phase builds the missing piece end-to-end: a new public page where any
user (anonymous or logged in) can open a form via its link, answer the
questions, and submit — without being able to modify the form itself. Viewing
aggregated results/analysis is explicitly out of scope for this phase (no
existing plumbing for that yet either — separate future phase).

**Decisions locked in for this phase:**
1. After submitting, the respondent just sees a plain thank-you message — no
   score or correct/incorrect breakdown shown (even though the backend will
   still *compute* a score internally, since that's the only point in the
   pipeline where `CorrectAnswer`/`IsCorrect` and the respondent's actual
   answers are both in scope — surfacing it in the UI is left for a future
   "view results" phase).
2. If a respondent reopens the link after already submitting (same anonymous
   `respondentToken` or same logged-in user), the page checks on load and
   shows "already responded" instead of the form.
3. No share-link UI is added anywhere (no "copy link" button on the
   dashboard). The route itself is the only new discoverability mechanism —
   the owner shares the URL manually. This can be revisited later.

---

## Part A — Domain: two small gaps that block building the aggregate

`Form`/`FormQuestion` already support the "create parent, then attach
children by their now-known Id" pattern used everywhere in this codebase
(`Form.ReplaceQuestions`, `FormQuestion.SetOptions` — see
`GenerateFormHandler.cs`/`SaveFormEditorHandler.cs`). `Submission`/`Answer` do
**not** have the equivalent, and `SubmitFormHandler` (Part E) needs it —
without it there's no way to attach `Answer`s to a `Submission`, or
`AnswerSelectedOption`s to an `Answer`, once their real (client-generated)
Ids exist.

### A1. `src/FormAI.Domain/Entities/Submission.cs`

Change `Answers` from nullable to a normal owned-collection property (mirrors
`Form.Questions`), and add a setter method (mirrors `Form.ReplaceQuestions`):

```csharp
public List<Answer> Answers { get; private set; } = new();
```

```csharp
public void SetAnswers(List<Answer> answers)
{
    Answers.Clear();
    Answers.AddRange(answers);
}
```

### A2. `src/FormAI.Domain/Entities/Answer.cs`

Same pattern for `SelectedOptions` (mirrors `FormQuestion.Options`/`SetOptions`):

```csharp
public List<AnswerSelectedOption> SelectedOptions { get; private set; } = new();
```

```csharp
public void SetSelectedOptions(List<AnswerSelectedOption> options)
{
    SelectedOptions.Clear();
    SelectedOptions.AddRange(options);
}
```

In `Create(...)`, change the assignment so passing `null` still works:

```csharp
SelectedOptions = selectedOptions ?? new()
```

No EF config changes and no migration needed — these are C#-only shape
changes; the DB column/FK setup in `AnswerConfiguration.cs`/
`AnswerSelectedOptionConfiguration.cs` is unaffected.

---

## Part B — Backend: `ISubmissionRepository`

### B1. `src/FormAI.Application/Interfaces/ISubmissionRepository.cs` (new)

```csharp
using FormAI.Domain.Entities;

namespace FormAI.Application.Interfaces;

public interface ISubmissionRepository
{
    Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
        CancellationToken cancellationToken = default);
    Task AddAsync(Submission submission, CancellationToken cancellationToken = default);
}
```

`GetByRespondentAsync` is the single lookup both the duplicate-check (Part E)
and the "already responded" pre-check (Part D) share: if the caller is
authenticated, match by `(FormId, UserId)`; otherwise match by
`(FormId, RespondentToken)` — same identity rule CLAUDE.md already documents
("duplicate submission blocked by userId (authenticated) or respondentToken +
IP (anonymous)"); IP is additionally enforced at the DB level via the existing
unique index (`SubmissionConfiguration.cs:25-26`) as a second layer.

### B2. `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs` (new)

```csharp
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class SubmissionRepository : ISubmissionRepository
{
    private readonly AppDbContext _context;

    public SubmissionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Submission?> GetByRespondentAsync(Guid formId, Guid? userId, Guid respondentToken,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Submission> query = _context.Submissions.Where(s => s.FormId == formId);

        query = userId is not null
            ? query.Where(s => s.UserId == userId)
            : query.Where(s => s.RespondentToken == respondentToken);

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Submission submission, CancellationToken cancellationToken = default)
    {
        await _context.Submissions.AddAsync(submission, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

`AddAsync(submission)` cascades to `Answers`/`SelectedOptions` automatically
via EF's change-tracker graph walk, same as `_repository.AddAsync(form, ...)`
already does for `Form.Questions`/`FormQuestion.Options` in
`GenerateFormHandler.cs` — no extra `AddRangeAsync` calls needed.

### B3. `src/FormAI.Infrastructure/DependencyInjection.cs` — register it

Alongside the other repository registrations:

```csharp
services.AddScoped<ISubmissionRepository, SubmissionRepository>();
```

---

## Part C — Backend: fetch a form to answer (no answer key)

New use case, deliberately separate from `GetFormHandler` (which the editor
uses and which legitimately needs `CorrectAnswer`/`IsCorrect` for editing).

### C1. `src/FormAI.Application/Submissions/GetFormToAnswer/GetFormToAnswerRequest.cs` (new)

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.GetFormToAnswer;

public record GetFormToAnswerRequest(Guid FormId, Guid? RequestingUserId);

public record GetFormToAnswerResponse(
    Guid Id,
    string Title,
    string? Description,
    bool IsExpired,
    List<AnswerQuestionDTO> Questions
);

public record AnswerQuestionDTO(
    Guid Id,
    string Text,
    QuestionType Type,
    int Order,
    bool IsRequired,
    List<AnswerOptionDTO> Options
);

public record AnswerOptionDTO(Guid Id, string Text, int Order);
```

No `CorrectAnswer`, no `IsCorrect`, no `Points` — nothing that would reveal
grading information to a respondent before they submit.

### C2. `src/FormAI.Application/Submissions/GetFormToAnswer/GetFormToAnswerHandler.cs` (new)

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Submissions.GetFormToAnswer;

public class GetFormToAnswerHandler
{
    private readonly IFormRepository _forms;

    public GetFormToAnswerHandler(IFormRepository forms)
    {
        _forms = forms;
    }

    public async Task<GetFormToAnswerResponse> HandleAsync(GetFormToAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form is null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic && form.CreatedBy != request.RequestingUserId)
            throw new NotFoundException("You don't have access to this form.");

        var questions = form.Questions.OrderBy(q => q.Order).Select(q =>
            new AnswerQuestionDTO(q.Id, q.Text, q.Type, q.Order, q.IsRequired,
                q.Options.OrderBy(o => o.Order)
                    .Select(o => new AnswerOptionDTO(o.Id, o.Text, o.Order))
                    .ToList()))
            .ToList();

        return new GetFormToAnswerResponse(form.Id, form.Title, form.Description, form.IsExpired, questions);
    }
}
```

Same access rule as `GetFormHandler` (public, or owner) → `NotFoundException`
(404) otherwise, matching the existing behavior respondents would already hit
on a private form. Expired forms are **not** blocked here — `IsExpired` is
returned so the frontend can render a friendly "no longer accepting
responses" message using the form's own title, instead of a bare error.

### C3. DI registration

```csharp
services.AddScoped<GetFormToAnswerHandler>();
```

---

## Part D — Backend: "have I already responded?" pre-check

### D1. `src/FormAI.Application/Submissions/GetMySubmission/GetMySubmissionRequest.cs` (new)

```csharp
namespace FormAI.Application.Submissions.GetMySubmission;

public record GetMySubmissionRequest(Guid FormId, Guid? RequestingUserId, Guid RespondentToken);

public record GetMySubmissionResponse(bool HasSubmitted, DateTime? SubmittedAt);
```

### D2. `src/FormAI.Application/Submissions/GetMySubmission/GetMySubmissionHandler.cs` (new)

```csharp
using FormAI.Application.Interfaces;

namespace FormAI.Application.Submissions.GetMySubmission;

public class GetMySubmissionHandler
{
    private readonly ISubmissionRepository _submissions;

    public GetMySubmissionHandler(ISubmissionRepository submissions)
    {
        _submissions = submissions;
    }

    public async Task<GetMySubmissionResponse> HandleAsync(GetMySubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        var submission = await _submissions.GetByRespondentAsync(
            request.FormId, request.RequestingUserId, request.RespondentToken, cancellationToken);

        return submission is null
            ? new GetMySubmissionResponse(false, null)
            : new GetMySubmissionResponse(true, submission.SubmittedAt);
    }
}
```

Returns `200` either way (not-yet-submitted isn't an error condition) — the
frontend branches on `hasSubmitted`, not on HTTP status.

### D3. DI registration

```csharp
services.AddScoped<GetMySubmissionHandler>();
```

---

## Part E — Backend: submit answers (validate, score, persist)

### E1. `src/FormAI.Application/Submissions/SubmitForm/SubmitFormRequest.cs` — extend

Add the two fields the controller fills in server-side (mirrors how
`SaveFormEditorRequest`/`UpdateFormRequest` get `FormId`/`RequestingUserId`
injected via `request with { ... }` in `FormsController`):

```csharp
public record SubmitFormRequest(
    Guid FormId,
    Guid? UserId,
    string IpAddress,
    Guid RespondentToken,
    IReadOnlyList<AnswerRequest> Answers
);
```

(`AnswerRequest`/`SubmitFormResponse` stay as they are.)

### E2. `src/FormAI.Application/Submissions/SubmitForm/SubmitFormHandler.cs` (new)

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Submissions.SubmitForm;

public class SubmitFormHandler
{
    private readonly IFormRepository _forms;
    private readonly ISubmissionRepository _submissions;

    public SubmitFormHandler(IFormRepository forms, ISubmissionRepository submissions)
    {
        _forms = forms;
        _submissions = submissions;
    }

    public async Task<SubmitFormResponse> HandleAsync(SubmitFormRequest request,
        CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(request.FormId, cancellationToken);
        if (form is null)
            throw new NotFoundException("Form not found.");

        if (!form.IsPublic && form.CreatedBy != request.UserId)
            throw new NotFoundException("You don't have access to this form.");

        if (form.IsExpired)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["form"] = new[] { "This form is no longer accepting responses." }
            });

        var existing = await _submissions.GetByRespondentAsync(
            form.Id, request.UserId, request.RespondentToken, cancellationToken);
        if (existing is not null)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["form"] = new[] { "You have already submitted a response for this form." }
            });

        var answersByQuestion = request.Answers.ToDictionary(a => a.QuestionId);
        var knownQuestionIds = form.Questions.Select(q => q.Id).ToHashSet();
        var errors = new Dictionary<string, string[]>();

        if (answersByQuestion.Keys.Any(id => !knownQuestionIds.Contains(id)))
            errors["answers"] = new[] { "One or more answers reference a question that isn't part of this form." };

        var results = new List<(FormQuestion Question, AnswerRequest Answer, int? Score, Guid[] SelectedOptionIds)>();

        foreach (var question in form.Questions)
        {
            answersByQuestion.TryGetValue(question.Id, out var answer);
            var isAnswered = IsAnswered(question.Type, answer);

            if (question.IsRequired && !isAnswered)
            {
                errors[question.Id.ToString()] = new[] { "This question is required." };
                continue;
            }

            if (!isAnswered)
                continue;

            var (score, selectedOptionIds) = ScoreAnswer(question, answer!);
            results.Add((question, answer!, score, selectedOptionIds));
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);

        int? totalScore = results.Any(r => r.Score is not null)
            ? results.Sum(r => r.Score ?? 0)
            : null;

        var submission = Submission.Create(form.Id, request.UserId, request.RespondentToken,
            request.IpAddress, totalScore);

        var answers = results.Select(r =>
        {
            var answer = Answer.Create(submission.Id, r.Question.Id, null,
                r.Answer.TextValue, (double?)r.Answer.NumericValue, r.Score);
            answer.SetSelectedOptions(r.SelectedOptionIds
                .Select(optionId => AnswerSelectedOption.Create(answer.Id, optionId))
                .ToList());
            return answer;
        }).ToList();

        submission.SetAnswers(answers);
        await _submissions.AddAsync(submission, cancellationToken);

        return new SubmitFormResponse(submission.Id, totalScore);
    }

    private static bool IsAnswered(QuestionType type, AnswerRequest? answer)
    {
        if (answer is null) return false;
        return type switch
        {
            QuestionType.Single or QuestionType.Multiple => answer.SelectedOptionIds is { Length: > 0 },
            QuestionType.Text => !string.IsNullOrWhiteSpace(answer.TextValue),
            QuestionType.Numeric => answer.NumericValue is not null,
            _ => false
        };
    }

    private static (int? Score, Guid[] SelectedOptionIds) ScoreAnswer(FormQuestion question, AnswerRequest answer)
    {
        switch (question.Type)
        {
            case QuestionType.Single:
            case QuestionType.Multiple:
                var selectedIds = answer.SelectedOptionIds ?? Array.Empty<Guid>();
                var isGraded = question.Options.Any(o => o.IsCorrect);
                if (!isGraded)
                    return (null, selectedIds);

                var correctIds = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet();
                var isCorrect = selectedIds.ToHashSet().SetEquals(correctIds);
                return (isCorrect ? question.Points ?? 0 : 0, selectedIds);

            case QuestionType.Text:
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                    return (null, Array.Empty<Guid>());
                var textMatch = string.Equals(answer.TextValue?.Trim(), question.CorrectAnswer.Trim(),
                    StringComparison.OrdinalIgnoreCase);
                return (textMatch ? question.Points ?? 0 : 0, Array.Empty<Guid>());

            case QuestionType.Numeric:
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer) || answer.NumericValue is null)
                    return (null, Array.Empty<Guid>());
                var numericMatch = double.TryParse(question.CorrectAnswer, out var expected)
                    && (double)answer.NumericValue.Value == expected;
                return (numericMatch ? question.Points ?? 0 : 0, Array.Empty<Guid>());

            default:
                return (null, Array.Empty<Guid>());
        }
    }
}
```

Notes on the design:
- `ValidationException` (not `ArgumentException`) is used for all
  rejections — checking `ExceptionHandlingMiddleware.cs`, only
  `NotFoundException`/`ForbiddenException`/`ValidationException`/
  `UnauthorizedAccessException` are mapped to specific status codes;
  `ArgumentException` (used in `GenerateFormHandler`/`SaveFormEditorHandler`
  for title validation) actually falls through to the generic 500 case today.
  `ValidationException`'s `Errors: Record<string,string[]>` shape also
  already matches the frontend's existing `CustomResponse`/
  `MessageErrorResponse` type (`frontend/src/types/CustomResponse.ts`), so no
  new frontend error-parsing code is needed.
- Scoring is computed in a side channel (`results` list) before any
  `Submission`/`Answer` entities are constructed, because `Submission.Create`
  takes `score` as a constructor argument — the total has to be known
  upfront, which means validating everything (including per-question scoring)
  before creating anything.
- A question with no marked-correct option (`Single`/`Multiple`) or a blank
  `CorrectAnswer` (`Text`/`Numeric`) is treated as **ungraded** — `Score`
  stays `null` for that answer and it doesn't contribute to
  `Submission.Score`. If nothing in the form is graded, `Submission.Score` is
  `null` overall (not `0`) — consistent with `Submission.Score` already being
  nullable.
- This computes and stores a score, but per the locked-in decision, the
  frontend (Part G) does not display it in this phase.

### E3. `src/FormAI.API/Controllers/SubmissionsController.cs` — implement

```csharp
using System.Security.Claims;
using FormAI.Application.Submissions.GetFormToAnswer;
using FormAI.Application.Submissions.GetMySubmission;
using FormAI.Application.Submissions.SubmitForm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FormAI.API.Controllers;

[ApiController]
[Route("api/forms/{formId:guid}")]
public class SubmissionsController : ControllerBase
{
    private readonly GetFormToAnswerHandler _getFormToAnswer;
    private readonly GetMySubmissionHandler _getMySubmission;
    private readonly SubmitFormHandler _submitForm;

    public SubmissionsController(GetFormToAnswerHandler getFormToAnswer,
        GetMySubmissionHandler getMySubmission, SubmitFormHandler submitForm)
    {
        _getFormToAnswer = getFormToAnswer;
        _getMySubmission = getMySubmission;
        _submitForm = submitForm;
    }

    private Guid? CurrentUserIdOrNull
    {
        get
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return sub is not null ? Guid.Parse(sub) : null;
        }
    }

    // GET /api/forms/{formId}/answer
    [HttpGet("answer")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFormToAnswer(Guid formId, CancellationToken cancellationToken)
    {
        var response = await _getFormToAnswer.HandleAsync(
            new GetFormToAnswerRequest(formId, CurrentUserIdOrNull), cancellationToken);
        return Ok(response);
    }

    // GET /api/forms/{formId}/my-submission?respondentToken={token}
    [HttpGet("my-submission")]
    [AllowAnonymous]
    public async Task<IActionResult> GetMySubmission(Guid formId, [FromQuery] Guid respondentToken,
        CancellationToken cancellationToken)
    {
        var response = await _getMySubmission.HandleAsync(
            new GetMySubmissionRequest(formId, CurrentUserIdOrNull, respondentToken), cancellationToken);
        return Ok(response);
    }

    // POST /api/forms/{formId}/submit
    [HttpPost("submit")]
    [AllowAnonymous]
    public async Task<IActionResult> Submit(Guid formId, [FromBody] SubmitFormRequest request,
        CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var cmd = request with { FormId = formId, UserId = CurrentUserIdOrNull, IpAddress = ipAddress };
        var response = await _submitForm.HandleAsync(cmd, cancellationToken);
        return Ok(response);
    }
}
```

The controller has no class-level `[Authorize]` (unlike `FormsController`),
since every action here must tolerate anonymous callers — `[AllowAnonymous]`
is added per-action anyway purely for self-documentation, matching the intent
already shown by `FormsController.GetById`. The remaining stub routes in the
original comments (`GET /results`, `GET /submissions`,
`GET /submissions/{sid}`) are **owner-facing results/analysis endpoints** —
left as comments, out of scope for this phase.

### E4. DI registration

```csharp
services.AddScoped<SubmitFormHandler>();
```

(Import `FormAI.Application.Submissions.SubmitForm;` alongside the other
`using`s in `DependencyInjection.cs`.)

---

## Part F — Frontend: types and API client

### F1. `frontend/src/types/submission.ts` (new)

```ts
import type { QuestionType } from './form';

export interface AnswerOption {
    id: string;
    text: string;
    order: number;
}

export interface AnswerQuestion {
    id: string;
    text: string;
    type: QuestionType;
    order: number;
    isRequired: boolean;
    options: AnswerOption[];
}

export interface AnswerForm {
    id: string;
    title: string;
    description: string | null;
    isExpired: boolean;
    questions: AnswerQuestion[];
}

export interface MySubmission {
    hasSubmitted: boolean;
    submittedAt: string | null;
}

export interface AnswerPayload {
    selectedOptionIds?: string[];
    textValue?: string;
    numericValue?: number;
}

export interface SubmitFormPayload {
    respondentToken: string;
    answers: (AnswerPayload & { questionId: string })[];
}

export interface SubmitFormResult {
    submissionId: string;
    score: number | null;
}
```

### F2. `frontend/src/utils/respondentToken.ts` (new)

```ts
const STORAGE_KEY = 'respondentToken';

export function getRespondentToken(): string {
    let token = localStorage.getItem(STORAGE_KEY);
    if (!token) {
        token = crypto.randomUUID();
        localStorage.setItem(STORAGE_KEY, token);
    }
    return token;
}
```

One token per browser, reused across all forms — same pattern
`AddQuestionModal.tsx` already uses `crypto.randomUUID()` for, just persisted
this time. This is unrelated to the JWT `accessToken` in `axios.ts`/
`AuthProvider.tsx` — a logged-in user submitting still sends this token (the
backend ignores it for the duplicate check when `UserId` is present, but the
DB unique index on `(FormId, RespondentToken, IpAddress)` still applies, so
it's always required on the wire).

### F3. `frontend/src/api/submissions.ts` (new)

```ts
import api from './axios';
import type { AnswerForm, MySubmission, SubmitFormPayload, SubmitFormResult } from '../types/submission';

export async function getFormToAnswer(formId: string): Promise<AnswerForm> {
    const response = await api.get<AnswerForm>(`/forms/${formId}/answer`);
    return response.data;
}

export async function getMySubmission(formId: string, respondentToken: string): Promise<MySubmission> {
    const response = await api.get<MySubmission>(`/forms/${formId}/my-submission`, {
        params: { respondentToken }
    });
    return response.data;
}

export async function submitForm(formId: string, payload: SubmitFormPayload): Promise<SubmitFormResult> {
    const response = await api.post<SubmitFormResult>(`/forms/${formId}/submit`, payload);
    return response.data;
}
```

Uses the same shared `api` axios instance as `forms.ts` — its request
interceptor already attaches `Authorization` when an `accessToken` exists and
simply omits it otherwise, so authenticated and anonymous respondents both
work through the same client without special-casing here. (The response
interceptor's hard-redirect-to-`/login` on `401` only fires when a stale JWT
is rejected — it never fires for anonymous requests, since those don't send
a token at all.)

---

## Part G — Frontend: the respond page

### G1. `frontend/src/components/respond/QuestionAnswerCard.tsx` (new)

A read-only-*about-the-question*, interactive-*about-the-answer* card — no
drag handle, no delete button, no label editing, no correct-answer marking
(unlike `components/editors/QuestionCard.tsx`, which is answer-*key* editing,
not answer *entry*):

```tsx
import type { AnswerQuestion } from '../../types/submission';
import type { AnswerPayload } from '../../types/submission';

interface QuestionAnswerCardProps {
    question: AnswerQuestion;
    index: number;
    answer?: AnswerPayload;
    error?: string;
    onSingleChange: (optionId: string) => void;
    onMultipleToggle: (optionId: string) => void;
    onTextChange: (value: string) => void;
    onNumericChange: (value: string) => void;
}

export function QuestionAnswerCard({
    question, index, answer, error,
    onSingleChange, onMultipleToggle, onTextChange, onNumericChange
}: QuestionAnswerCardProps) {
    return (
        <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
            <p className="text-sm font-medium text-gray-900">
                {index + 1}. {question.text}
                {question.isRequired && <span className="text-red-500"> *</span>}
            </p>

            <div className="mt-3 flex flex-col gap-2">
                {question.type === 'Single' && question.options.map(opt => (
                    <label key={opt.id} className="flex items-center gap-2 text-sm text-gray-700">
                        <input
                            type="radio"
                            name={question.id}
                            checked={answer?.selectedOptionIds?.[0] === opt.id}
                            onChange={() => onSingleChange(opt.id)}
                            className="text-brand-600 focus:ring-brand-500"
                        />
                        {opt.text}
                    </label>
                ))}

                {question.type === 'Multiple' && question.options.map(opt => (
                    <label key={opt.id} className="flex items-center gap-2 text-sm text-gray-700">
                        <input
                            type="checkbox"
                            checked={answer?.selectedOptionIds?.includes(opt.id) ?? false}
                            onChange={() => onMultipleToggle(opt.id)}
                            className="rounded text-brand-600 focus:ring-brand-500"
                        />
                        {opt.text}
                    </label>
                ))}

                {question.type === 'Text' && (
                    <textarea
                        rows={3}
                        value={answer?.textValue ?? ''}
                        onChange={e => onTextChange(e.target.value)}
                        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
                    />
                )}

                {question.type === 'Numeric' && (
                    <input
                        type="number"
                        value={answer?.numericValue ?? ''}
                        onChange={e => onNumericChange(e.target.value)}
                        className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm shadow-sm outline-none
                                   focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
                    />
                )}
            </div>

            {error && <p className="mt-2 text-xs text-red-500">{error}</p>}
        </div>
    );
}
```

### G2. `frontend/src/pages/FormRespondPage.tsx` (new)

```tsx
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { getFormToAnswer, getMySubmission, submitForm } from '../api/submissions';
import { getRespondentToken } from '../utils/respondentToken';
import { QuestionAnswerCard } from '../components/respond/QuestionAnswerCard';
import { Button } from '../components/Button';
import type { AnswerForm, AnswerQuestion, AnswerPayload } from '../types/submission';
import type { CustomResponse } from '../types/CustomResponse';

type PageState = 'loading' | 'alreadySubmitted' | 'expired' | 'ready' | 'submitting' | 'submitted' | 'error';

function isAnswered(question: AnswerQuestion, answer?: AnswerPayload): boolean {
    if (!answer) return false;
    switch (question.type) {
        case 'Single':
        case 'Multiple':
            return (answer.selectedOptionIds?.length ?? 0) > 0;
        case 'Text':
            return !!answer.textValue?.trim();
        case 'Numeric':
            return answer.numericValue !== undefined;
    }
}

export function FormRespondPage() {
    const { id } = useParams<{ id: string }>();
    const [respondentToken] = useState(getRespondentToken());

    const [state, setState] = useState<PageState>('loading');
    const [form, setForm] = useState<AnswerForm | null>(null);
    const [answers, setAnswers] = useState<Record<string, AnswerPayload>>({});
    const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
    const [submitError, setSubmitError] = useState('');

    useEffect(() => {
        if (!id) return;

        getMySubmission(id, respondentToken)
            .then(sub => {
                if (sub.hasSubmitted) {
                    setState('alreadySubmitted');
                    return;
                }
                return getFormToAnswer(id).then(data => {
                    setForm(data);
                    setState(data.isExpired ? 'expired' : 'ready');
                });
            })
            .catch(() => setState('error'));
    }, [id, respondentToken]);

    function setSingleAnswer(questionId: string, optionId: string) {
        setAnswers(a => ({ ...a, [questionId]: { selectedOptionIds: [optionId] } }));
    }

    function toggleMultipleAnswer(questionId: string, optionId: string) {
        setAnswers(a => {
            const current = a[questionId]?.selectedOptionIds ?? [];
            const next = current.includes(optionId)
                ? current.filter(o => o !== optionId)
                : [...current, optionId];
            return { ...a, [questionId]: { selectedOptionIds: next } };
        });
    }

    function setTextAnswer(questionId: string, value: string) {
        setAnswers(a => ({ ...a, [questionId]: { textValue: value } }));
    }

    function setNumericAnswer(questionId: string, value: string) {
        setAnswers(a => ({ ...a, [questionId]: { numericValue: value === '' ? undefined : Number(value) } }));
    }

    async function handleSubmit() {
        if (!id || !form) return;

        const missing = form.questions.filter(q => q.isRequired && !isAnswered(q, answers[q.id]));
        if (missing.length > 0) {
            setFieldErrors(Object.fromEntries(missing.map(q => [q.id, 'This question is required.'])));
            return;
        }

        setFieldErrors({});
        setSubmitError('');
        setState('submitting');

        try {
            await submitForm(id, {
                respondentToken,
                answers: form.questions
                    .filter(q => isAnswered(q, answers[q.id]))
                    .map(q => ({ questionId: q.id, ...answers[q.id] }))
            });
            setState('submitted');
        } catch (err: unknown) {
            const e = err as CustomResponse;
            setSubmitError(e.response?.data?.message ?? 'Failed to submit. Please try again.');
            setState('ready');
        }
    }

    if (state === 'loading') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-gray-500">Loading form…</p>
            </div>
        );
    }

    if (state === 'alreadySubmitted') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-gray-600">You've already responded to this form. Thanks!</p>
            </div>
        );
    }

    if (state === 'expired') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-gray-600">This form is no longer accepting responses.</p>
            </div>
        );
    }

    if (state === 'submitted') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-gray-600">Thanks! Your response has been recorded.</p>
            </div>
        );
    }

    if (state === 'error' || !form) {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-red-600">Form not found or you don't have access.</p>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-gray-50">
            <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">
                <div>
                    <h1 className="text-xl font-bold text-gray-900">{form.title}</h1>
                    {form.description && <p className="mt-1 text-sm text-gray-500">{form.description}</p>}
                </div>

                {form.questions.map((q, i) => (
                    <QuestionAnswerCard
                        key={q.id}
                        question={q}
                        index={i}
                        answer={answers[q.id]}
                        error={fieldErrors[q.id]}
                        onSingleChange={optionId => setSingleAnswer(q.id, optionId)}
                        onMultipleToggle={optionId => toggleMultipleAnswer(q.id, optionId)}
                        onTextChange={value => setTextAnswer(q.id, value)}
                        onNumericChange={value => setNumericAnswer(q.id, value)}
                    />
                ))}

                {submitError && <p className="text-center text-sm text-red-600">{submitError}</p>}

                <Button onClick={handleSubmit} isLoading={state === 'submitting'}>
                    Submit
                </Button>
            </main>
        </div>
    );
}
```

Title/description are rendered as plain text (`<h1>`/`<p>`) — no inputs, no
edit affordance anywhere on this page, satisfying "user won't be able to
change title or description either."

### G3. `frontend/src/App.tsx` — add the route

```tsx
import { FormRespondPage } from './pages/FormRespondPage';
// ...
<Route path="/forms/:id/respond" element={<FormRespondPage />} />
```

Added alongside the other routes; no guard needed since none of the existing
routes use one either (auth is enforced per-page via `useAuth()`/401s, not at
the router level), and this route must work for anonymous users regardless.

---

## Verification

- Backend: `dotnet build FormAI.sln`.
  - `GET /api/forms/{id}/answer` on a public form, anonymous → `200`, no
    `correctAnswer`/`isCorrect` anywhere in the response.
  - `GET /api/forms/{id}/answer` on a private form, anonymous or non-owner →
    `404`. As the owner → `200`.
  - `GET /api/forms/{id}/answer` on an expired form → `200` with
    `isExpired: true` (not blocked).
  - `GET /api/forms/{id}/my-submission?respondentToken=...` before any
    submission → `200 { hasSubmitted: false, submittedAt: null }`.
  - `POST /api/forms/{id}/submit` (anonymous, public form) with all required
    questions answered → `200`, `submissionId` returned; row appears in
    `Submissions`/`Answers`/`AnswerSelectedOptions`.
  - Same request again with the same `respondentToken` → `400` with
    `errors.form: ["You have already submitted..."]`, and
    `GET .../my-submission` now returns `hasSubmitted: true`.
  - Submit with a required question left blank → `400` with a per-question
    error keyed by that question's id.
  - Submit against an expired form → `400`,
    `errors.form: ["This form is no longer accepting responses."]`.
  - Submit against a private form as a non-owner → `404`.
  - Submit a `Single`-choice answer matching the option marked
    `isCorrect: true` (created via the editor beforehand) → response `score`
    reflects the question's `points`; a wrong answer → `score: 0` for that
    question, correctly summed into `Submission.Score`.
- Frontend: run the dev server (`npm run dev`), backend running too.
  - As the form owner: generate/edit a form with a mix of question types
    (some required, some not; mark a correct answer on at least one), make it
    public, copy its id, open `/forms/{id}/respond` in an incognito window
    (anonymous).
  - Confirm the page shows title/description as **plain text** (no inputs),
    renders each question with the right input type, and required questions
    are marked.
  - Try submitting with a required question blank → inline error under that
    question, no request sent.
  - Fill in everything, submit → "Thanks! Your response has been recorded."
    (no score/correct-answer shown, per the locked-in scope).
  - Reload `/forms/{id}/respond` in the same window → "You've already
    responded to this form." (localStorage `respondentToken` persisted).
  - Open the same link in a fresh incognito window (new token) → form loads
    again as a new respondent, can submit independently.
  - Set the form's `expiresAt` in the past (via the editor or DB) and reload
    the respond link → "This form is no longer accepting responses."
  - Make the form private and reload as a non-owner → "Form not found or you
    don't have access."
