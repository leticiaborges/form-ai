# Phase 26: Owner Submission List + Detail API

## Overview

Add two new owner-only endpoints to let a form owner browse individual submissions and view the full detail of each submission (what was answered per question and whether it was correct).

## API Contract Design

### 1. Routes and HTTP verbs

Both endpoints live on `FormsController` (owner-only, class-level `[Authorize]`), alongside the existing `/submissions/count` and `/results` routes:

- `GET /api/forms/{id:guid}/submissions` — paginated list of submission summaries.
- `GET /api/forms/{id:guid}/submissions/{submissionId:guid}` — one submission's full detail.

**Rationale: nest under `/submissions`, not singular `/submission/{submissionId}`.**

`FormsController` already exposes `GET /api/forms/{id:guid}/submissions/count` — so `/submissions` is the established collection route. Nesting the item route under that same collection (`/submissions/{submissionId}`) follows the standard REST collection+member pattern and keeps one mental model. The URI constraint `{submissionId:guid}` is matched after the literal `count` segment, exactly as today, so there's no routing ambiguity.

### 2. Pagination contract (first paging convention in this codebase)

**Query params:** `page` (1-based, default 1), `pageSize` (default 20, max 100).

Why `page`/`pageSize` over `skip`/`take`? More conventional for browsing UIs ("page 3 of 7") and easier to reason about than raw offsets. No cursor-based approach needed for this data volume.

**Response envelope — introduce a reusable generic wrapper:**

```csharp
// FormAI.Application/Common/PagedResult.cs

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount
)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
```

Place in `FormAI.Application/Common/` — reusable for any future paginated endpoint, and the Application layer is the right spot since Domain has no DTOs.

**Sort order:** newest-first by `SubmittedAt` (`OrderByDescending`). **OPEN DECISION** — confirm this or propose an alternative (oldest-first, sorted by score, etc.).

### 3. List endpoint response shape

**OPEN DECISION:** You asked for "only submission ids," but a bare `Guid[]` gives your UI nothing to browse or triage by. 

**Recommended payload:**
```csharp
public record SubmissionSummaryResponse(
    Guid SubmissionId,
    DateTime SubmittedAt,
    int? Score  // null when form ungraded
);

// Response is PagedResult<SubmissionSummaryResponse>
```

This keeps the endpoint light (no answers, no per-question data) while giving the UI something meaningful to display and sort. If you want strictly minimal, the response can stay as `PagedResult<Guid>` instead — straightforward downgrade.

### 4. Detail endpoint response shape

Refining your sketch with real domain field names:

```csharp
public record SubmissionDetailResponse(
    Guid SubmissionId,
    DateTime SubmittedAt,
    int? Score,  // submission-level; null = form ungraded
    List<SubmissionAnswerResponse> Answers
);

public record SubmissionAnswerResponse(
    Guid QuestionId,
    string QuestionText,      // OPEN DECISION — added for context
    QuestionType Type,        // OPEN DECISION — added for context
    List<string> SelectedOptions,  // empty for Text/Numeric
    string? TextValue,
    double? NumericValue,
    bool? IsCorrect,          // null when ungraded; else computed via SubmissionScorer
    int? Points,              // OPEN DECISION — question's max points
    int? AnswerScore          // OPEN DECISION — this answer's earned score
);
```

Key changes from your sketch:

- **Split generic `value` into `selectedOptions` / `textValue` / `numericValue`** — mirrors `Answer` entity fields and avoids client guessing which is populated per `QuestionType`.
- **`isCorrect` is computed, never read from `Answer.Score`** — call `SubmissionScorer.IsAnswerCorrect(question, ScoredAnswer.From(answer))` per CLAUDE.md's rule. Null when form ungraded; `false` (not null) when graded but no answer key exists.
- **Skipped questions omitted** — consistent with `FormResultsCalculator`; a skipped question has no `Answer` row, so it won't appear in the array.
- **Added `questionText`, `type`, `points`, `answerScore`** — make the response self-contained (no extra call to `GET /api/forms/{id}` needed to label answers). **OPEN DECISION:** trim these if the frontend already has the question list loaded elsewhere.
- **Respondent identity deliberately left out** — no `userId` / `email` / `ipAddress` / anonymous-flag. **OPEN DECISION:** decide whether the owner's detail view needs to show who submitted and whether raw `IpAddress` should ever be exposed through this API.

### 5. Repository additions (`ISubmissionRepository`)

Two new methods:

```csharp
/// <summary>
/// A page of submissions for a form, newest first by SubmittedAt.
/// Untracked, no Include — deliberately id/metadata only for the list view.
/// </summary>
Task<(IReadOnlyList<Submission> Items, int TotalCount)> GetPagedByFormAsync(
    Guid formId, int page, int pageSize, CancellationToken cancellationToken = default);

/// <summary>
/// One submission with its answers and selected options, scoped to a form 
/// (a submission id from another form's URL cannot be fetched cross-form).
/// Untracked, for the detail view.
/// </summary>
Task<Submission?> GetByIdWithAnswersAsync(
    Guid formId, Guid submissionId, CancellationToken cancellationToken = default);
```

EF query shapes:

- **`GetPagedByFormAsync`**: `AsNoTracking()`, `Where(s => s.FormId == formId)`, `OrderByDescending(s => s.SubmittedAt)`, `Skip((page-1)*pageSize)`, `Take(pageSize)`. Paired with separate `CountAsync(s => s.FormId == formId)` for total — two round trips, matching the existing standalone-count pattern. Returns full `Submission` entities (not just ids) because the recommended list summary needs `SubmittedAt`/`Score`.

- **`GetByIdWithAnswersAsync`**: `AsNoTracking()`, `AsSplitQuery()`, `Include(s => s.Answers).ThenInclude(a => a.SelectedOptions)`, `Where(s => s.FormId == formId && s.Id == submissionId)`, `FirstOrDefaultAsync()`. Same shape as the existing `GetByFormWithAnswersAsync`, just scoped to one row. The compound `Where` (both `formId` and `submissionId`) ensures a cross-form submission id returns 404 instead of leaking.

Both methods are additive — no existing method signatures change.

### 6. Handler responsibilities

**`FormAI.Application/Forms/GetFormSubmissions/`:**
- Request: `record GetFormSubmissionsRequest(Guid FormId, Guid RequestingUserId, int Page, int PageSize)`.
- Handler:
  1. Run the canonical 3-step ownership check (copy from `GetFormResultsHandler`): `NotFoundException` when private and not owned, `ForbiddenException` when published and not owned.
  2. Clamp `page`/`pageSize` (see Open Decision #6 below).
  3. Call `_submissions.GetPagedByFormAsync(formId, page, pageSize, ct)`.
  4. Map each `Submission` to `SubmissionSummaryResponse`, wrap in `PagedResult<SubmissionSummaryResponse>`.

**`FormAI.Application/Forms/GetFormSubmissionDetail/`:**
- Request: `record GetFormSubmissionDetailRequest(Guid FormId, Guid SubmissionId, Guid RequestingUserId)`.
- Handler:
  1. Same 3-step ownership check as above.
  2. Call `_submissions.GetByIdWithAnswersAsync(formId, submissionId, ct)`.
  3. If null, throw `NotFoundException("Submission not found")` — covers both "doesn't exist" and "belongs to different form".
  4. For each `Answer`, resolve its `FormQuestion` from `form.Questions`.
  5. Build `ScoredAnswer.From(answer)`, compute `isCorrect` / `answerScore` only `if (form.IsGraded)`.
  6. Call `SubmissionScorer.IsAnswerCorrect(question, scoredAnswer)` to compute correctness per CLAUDE.md's "recomputed, not read" rule.
  7. Order answers by `question.Order`, map to `SubmissionAnswerResponse`, wrap in `SubmissionDetailResponse`.

Both handlers inject only `IFormRepository` and `ISubmissionRepository` — same two dependencies `GetFormResultsHandler` already takes.

### 7. DI registration

In `src/FormAI.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<GetFormSubmissionsHandler>();
services.AddScoped<GetFormSubmissionDetailHandler>();
```

### 8. Edge cases

- **Submission belongs to a different form than `{formId}`:** Identical to "submission id doesn't exist" — `GetByIdWithAnswersAsync` filters on both `formId` and `submissionId`, so mismatch returns null, which becomes a 404. Avoids confirming existence to a non-owning caller.
- **Submission id doesn't exist:** Same 404 path, indistinguishable from the mismatch case.
- **Page beyond the last page:** `PagedResult` comes back with empty `Items` but correct metadata — not an error.
- **Form is ungraded:** Submission-level `score` is null, every answer's `isCorrect`/`answerScore` is null (never `false`/`0`).
- **Form has no submissions:** List returns `TotalCount: 0` with empty `Items` — not a 404.
- **`page`/`pageSize` out of range:** **OPEN DECISION** — recommend silent clamping (`page = max(1, page)`, `pageSize ∈ [1,100]`) so a stale "next page" click doesn't 400. Alternative: strict `ValidationException`.

## Open Decisions

1. **List payload:** Keep bare `Guid[]` or add `submittedAt`/`score` per summary response? (Recommended: add summary fields.)

2. **Sort order:** Newest-first, oldest-first, by score, or something else?

3. **Pagination style:** `page`/`pageSize` + `PagedResult<T>` (recommended) vs. `skip`/`take` vs. cursor-based?

4. **Detail endpoint context fields:** Include `questionText`, `type`, `points`, `answerScore`? (Recommended: yes, for self-contained response.)

5. **Respondent identity in detail:** Should the owner see who submitted (user id/email/anonymous), raw `IpAddress`, or neither? (Recommended: leave undecided; requires product decision.)

6. **Validation vs. clamping for pagination params:** Return 400 on invalid `page`/`pageSize` (strict), or silently clamp (forgiving)? (Recommended: clamp.)

## Critical Implementation Files

- `src/FormAI.Application/Interfaces/ISubmissionRepository.cs` — add two new method signatures.
- `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs` — implement the two methods.
- `src/FormAI.Application/Common/PagedResult.cs` — new file, the reusable paging envelope.
- `src/FormAI.Application/Forms/GetFormSubmissions/` — new use-case folder and three files (Request, Response, Handler).
- `src/FormAI.Application/Forms/GetFormSubmissionDetail/` — new use-case folder and three files.
- `src/FormAI.API/Controllers/FormsController.cs` — add two new routes.
- `src/FormAI.Infrastructure/DependencyInjection.cs` — register the two new handlers.

**Templates to follow:**
- `src/FormAI.Application/Forms/GetFormResults/` for handler structure and 3-step ownership check.
- `src/FormAI.Domain/Scoring/SubmissionScorer.cs` for correctness computation.
