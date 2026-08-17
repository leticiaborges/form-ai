# Phase 9: User-settable form title (create + edit)

## Context

Today the form title is never entered by the user:

- **Create/generate**: `GenerateFormRequest` has no title field. `GenerateFormHandler`
  always hardcodes `title: $"Generated Form – {DateTime.UtcNow:yyyy-MM-dd HH:mm}"`
  (`src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs:41`). The
  frontend's `CreateFormPage.tsx` only collects `sourceText`, `questionCount`,
  `difficultyLevel`, and `includeCorrectAnswers` — no title input exists.
- **Edit**: `FormEditorPage.tsx` renders the title read-only
  (`<h3 className="...">{form.title}</h3>`, line 109-111) and only ever calls
  `updateQuestions()`. There's no title input and no call to update form metadata.

The backend already has everything needed to edit a title — `UpdateFormRequest` /
`UpdateFormHandler` / `PUT /api/forms/{id}` (`FormsController.Update`) — it updates
`Title` today, it's just never called from the frontend edit flow. `Form.Title` is
already constrained to 255 chars at the DB level
(`FormConfiguration.cs:13-15`, `HasMaxLength(255)`), so no migration is needed —
this phase only needs matching application-level validation and the frontend UI to
actually let a user set/edit it.

**Requirements for this phase:**
1. On create, the user may optionally type a title (max 255 chars). If left empty,
   keep today's behavior — fall back to the auto-generated title.
2. On edit, the user can change the title of an existing form (max 255 chars,
   cannot be blank once the form exists — there's no generated-title fallback at
   edit time).

---

## Part A — settable title on create/generate

### A1. `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs` — add `Title`

```csharp
public record GenerateFormRequest(
    string? Title,
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    string DifficultyLevel,
    bool IncludeCorrectAnswers
);
```

Nullable/optional — omitting it (or sending `null`/whitespace) keeps the current
auto-generated title.

### A2. `GenerateFormHandler.cs` — use the provided title when present

```csharp
var title = string.IsNullOrWhiteSpace(request.Title)
    ? $"Generated Form – {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
    : request.Title.Trim();

if (title.Length > 255)
    throw new ArgumentException("Title must be at most 255 characters.");

var form = Form.Create(
    title: title,
    description: null,
    createdBy: requestingUserId,
    sourceType: SourceType.Text,
    isPublic: true,
    expiresAt: DateTime.UtcNow.AddDays(15),
    showResultsAfterSubmit: false
);
```

Same `ArgumentException` style already used a few lines above for the
`SourceText` check, so it's caught the same way by whatever global exception
middleware/handler already maps `ArgumentException` → 400.

### A3. `frontend/src/api/forms.ts` — pass `title` through `generateForm`

```ts
export interface GenerateFormPayload {
    title?: string;
    sourceText: string;
    questionCount: number;
    difficultyLevel: string;
    includeCorrectAnswers: boolean;
}

export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
    const response = await api.post<GenerateFormResult>('/forms/generate/text', {
        title: payload.title?.trim() || null,
        sourceText: payload.sourceText,
        sourceType: 'Text',
        sourceUrl: null,
        questionCount: payload.questionCount,
        allowedTypes: null,
        difficultyLevel: payload.difficultyLevel,
        includeCorrectAnswers: payload.includeCorrectAnswers,
    });
    return response.data;
}
```

Sending `null` for a blank/whitespace-only title (rather than an empty string)
matches `string.IsNullOrWhiteSpace` on the backend and makes the "use the
generated title" fallback explicit at the call site.

### A4. `frontend/src/pages/CreateFormPage.tsx` — add the optional title field

Extend the schema:

```ts
const createFormSchema = z.object({
  title: z.string().max(255, "Title must be at most 255 characters").optional(),
  sourceText: z.string().min(50, "Source text must be at least 50 characters long"),
  questionCount: z.coerce.number().int().min(1, "At least 1 question.").
    max(20, "At most 20 questions."),
  difficultyLevel: z.enum(["Easy", "Medium", "Hard"], "Difficulty level must be one of Easy, Medium, or Hard."),
  includeCorrectAnswers: z.boolean()
});
```

Add the input as the first field in the form, above "Source content":

```tsx
<div className="flex flex-col gap-1">
  <label htmlFor="title" className="text-sm font-medium text-gray-700">
    Form title <span className="text-gray-400 font-normal">(optional)</span>
  </label>
  <input
    id="title"
    type="text"
    maxLength={255}
    className={
      'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
      'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
      (errors.title ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
    }
    placeholder="Leave blank to use a generated title"
    {...register('title')}
  />
  {errors.title && (
    <span className="text-xs text-red-500">{errors.title.message}</span>
  )}
</div>
```

No change needed to `defaultValues` — `title` simply starts undefined.

---

## Part B — editable title after creation

`FormEditorPage`'s Save button needs to persist the title and the questions as
one atomic action. `UpdateFormHandler` and `UpdateQuestionsHandler` each call
`_forms.UpdateAsync(form, ...)` independently
(`src/FormAI.Infrastructure/Repositories/FormRepository.cs:46-50`), and each
call is its own `SaveChangesAsync` — i.e. its own DB transaction, on a `Form`
instance loaded fresh in that request's own `AppDbContext` scope. Firing both
from the frontend (even in parallel) means a partial failure is possible: the
title commits but the questions don't (or vice versa), and a refresh before
retrying would show a mixed state that doesn't match what the user thinks they
saved.

So instead of reusing both existing endpoints from the editor, add one new
combined use case — `SaveFormEditor` — that loads the form once and updates
title + questions in a single `SaveChangesAsync`. The existing `UpdateForm`
(`PUT /api/forms/{id}`) and `UpdateQuestions` (`PUT /api/forms/{id}/questions`)
endpoints are left as-is for other callers that only need to touch one or the
other (e.g. a future settings-only screen) — nothing is removed.

### B1. New `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs`

```csharp
using FormAI.Domain.Enums;

namespace FormAI.Application.Forms.SaveFormEditor;

public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    List<QuestionInput> Questions
);

public record QuestionInput(string Text, QuestionType Type, int Order, bool IsRequired,
    bool AiGenerated, int? Points, string? CorrectAnswer, List<OptionInput> Options);

public record OptionInput(string Text, int Order, bool IsCorrect);
```

`QuestionInput`/`OptionInput` mirror `UpdateQuestionsRequest`'s shapes
(`src/FormAI.Application/Forms/UpdateQuestions/UpdateQuestionsRequest.cs`) —
duplicated rather than shared, matching this codebase's existing convention of
each use case owning its own request types even when shapes overlap (compare
`GetFormResponse.QuestionDTO` vs. `GenerateFormResponse.GeneratedQuestionResponse`).

### B2. New `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs`

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

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

        form.Update(request.Title.Trim(), form.Description ?? string.Empty, form.IsPublic,
            form.ExpiresAt, form.ShowResultsAfterSubmit);

        var questions = request.Questions.Select(q =>
        {
            var question = FormQuestion.Create(request.FormId,
                q.Text, q.Type, q.Order, q.IsRequired, q.AiGenerated, q.Points, q.CorrectAnswer);

            var options = q.Options.Select(o => QuestionOption.Create(question.Id, o.Text, o.Order, o.IsCorrect))
                        .ToList();

            question.SetOptions(options);
            return question;
        }).ToList();

        form.ReplaceQuestions(questions);

        await _forms.UpdateAsync(form, cancellationToken);
    }
}
```

`form.Update(...)` is called with the form's *own current* description/
isPublic/expiresAt/showResultsAfterSubmit — this handler only changes title and
questions, so the frontend never needs to resend the rest of the metadata (a
side benefit over the two-request approach, which required shipping the whole
`FormDetail` back on every save). Both the title mutation and
`ReplaceQuestions` happen on the same tracked `form` instance before the single
`UpdateAsync` call, so they commit together in one `SaveChangesAsync`.

### B3. `src/FormAI.Infrastructure/DependencyInjection.cs` — register the handler

Alongside the existing registrations (line 61-62):

```csharp
services.AddScoped<SaveFormEditorHandler>();
```

### B4. `src/FormAI.API/Controllers/FormsController.cs` — new endpoint

```csharp
private readonly SaveFormEditorHandler _saveFormEditor;

public FormsController(CreateFormHandler create,
GetFormHandler getById, GetFormsByUserHandler getByUser,
UpdateFormHandler update, DeleteFormHandler delete, CloseFormHandler close,
UpdateQuestionsHandler updateQuestions,
GenerateFormHandler generateForm,
SaveFormEditorHandler saveFormEditor)
{
    // ...existing assignments...
    _saveFormEditor = saveFormEditor;
}

// PUT /api/forms/{id}/editor
[HttpPut("{id:guid}/editor")]
public async Task<IActionResult> SaveEditor(Guid id,
[FromBody] SaveFormEditorRequest request,
CancellationToken cancellationToken)
{
    var cmd = request with { FormId = id, RequestingUserId = CurrentUserId };
    await _saveFormEditor.HandleAsync(cmd, cancellationToken);
    return NoContent();
}
```

Add the `using FormAI.Application.Forms.SaveFormEditor;` import at the top.

### B5. `frontend/src/api/forms.ts` — replace the two calls with one

```ts
export interface SaveFormEditorPayload {
    title: string;
    questions: FormQuestion[];
}

export async function saveFormEditor(formId: string, payload: SaveFormEditorPayload): Promise<void> {
    await api.put(`/forms/${formId}/editor`, {
        title: payload.title,
        questions: payload.questions.map((q, i) => ({
            text: q.text,
            type: q.type,
            order: i + 1,
            isRequired: q.isRequired,
            aiGenerated: q.aiGenerated,
            points: q.points,
            correctAnswer: q.correctAnswer,
            options: q.options.map((o, oi) => ({
                text: o.text,
                order: oi + 1,
                isCorrect: o.isCorrect
            }))
        }))
    });
}
```

`updateForm`/`updateQuestions` stay in this file unchanged (still usable
elsewhere) — this just adds the combined call for the editor screen.

### B6. `frontend/src/pages/FormEditorPage.tsx` — editable title + single save

Add title state, seeded from the loaded form:

```tsx
import { getForm, saveFormEditor } from '../api/forms';
// ...
const [title, setTitle] = useState('');
const [titleError, setTitleError] = useState('');
// ...
getForm(id).then(data => {
    setForm(data);
    setQuestions(data.questions);
    setTitle(data.title);
    setState('ready');
}).catch(() => setState('error'));
```

Replace the read-only `<h3>` in the sticky header with an inline-editable input
(kept visually close to the current heading, just swapped for an `<input>`):

```tsx
<div>
    <input
        value={title}
        maxLength={255}
        onChange={e => { setTitle(e.target.value); setTitleError(''); }}
        className={
            'max-w-xs truncate text-base font-semibold text-gray-900 bg-transparent ' +
            'border border-transparent rounded px-1 -mx-1 outline-none ' +
            'hover:border-gray-200 focus:border-brand-400 focus:ring-1 focus:ring-brand-400 ' +
            (titleError ? 'border-red-400' : '')
        }
    />
    {titleError && <p className="text-xs text-red-500 px-1">{titleError}</p>}
    <p className="text-xs text-gray-400">
        {questions.length} question{questions.length !== 1 ? 's' : ''}
    </p>
</div>
```

Replace `handleSave`'s single `updateQuestions` call with the combined one:

```tsx
async function handleSave() {
    if (!id)
        return;

    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
        setTitleError('Title is required.');
        return;
    }
    if (trimmedTitle.length > 255) {
        setTitleError('Title must be at most 255 characters.');
        return;
    }

    setSaveError('');
    setState('saving');

    try {
        await saveFormEditor(id, { title: trimmedTitle, questions });
        setForm(f => f ? { ...f, title: trimmedTitle } : f);
        setState('ready');
    }
    catch {
        setSaveError('Failed to save. Please try again');
        setState('ready');
    }
}
```

One request, one transaction — either the whole editor save lands, or none of
it does.

---

## Verification

- Backend: `dotnet build FormAI.sln`.
  - `POST /api/forms/generate/text` with `title: "My quiz"` → created form's
    `title` is `"My quiz"`. With `title: null` or omitted → falls back to the
    `Generated Form – …` default, same as today.
  - `POST /api/forms/generate/text` with a 256-char `title` → `400`.
  - `PUT /api/forms/{id}/editor` with a valid `title` + `questions` (owner) →
    `204`, and `GET /api/forms/{id}` reflects both the new title and the new
    question list.
  - `PUT /api/forms/{id}/editor` with blank/whitespace `title` → `400`, and
    confirm the questions were *not* updated either (single-transaction
    rollback — nothing partially applied).
  - `PUT /api/forms/{id}/editor` with a 256-char `title` → `400`.
  - `PUT /api/forms/{id}/editor` for a form you don't own → `403`.
  - `PUT /api/forms/{id}` and `PUT /api/forms/{id}/questions` still work
    standalone (unaffected by this change).
- Frontend: run the dev server, log in.
  - On `/forms/new`: leave title blank, submit → generated form keeps the
    auto-generated title (visible on `/forms/:id/edit`). Enter a title →
    it's used verbatim. Enter >255 chars → inline validation error, no request
    sent.
  - On `/forms/:id/edit`: edit the title in the header, edit/add/remove a
    question, click Save → a single `PUT /forms/{id}/editor` request fires
    (check the network tab), title and questions both persist (reload the page
    to confirm it's not just local state), and the updated title also shows
    back on `/dashboard`'s form list. Clear the title entirely and click Save →
    inline error, no request sent, previous title *and* question edits remain
    unsaved (still visible in the editor, but not persisted until title is
    fixed and Save is pressed again).
