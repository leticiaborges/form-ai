# Phase 10: Default forms to private, add a public/private checkbox to the editor

## Context

Today every generated form is created **public** with no way to change that from
the UI:

- `GenerateFormHandler` (the only creation path the frontend actually uses —
  `CreateFormPage.tsx` calls `generateForm()`, never `createForm()`) hardcodes
  `isPublic: true` when building the form
  (`src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs:52`).
  `GenerateFormRequest` has no `IsPublic` field at all, so there's no way to
  pass a different value in even if the frontend wanted to.
- `SaveFormEditorHandler` — the handler wired to the editor's Save button
  (`PUT /api/forms/{id}/editor`) — already calls `form.Update(...)`, but it
  passes the form's **existing** `IsPublic` straight back unchanged
  (`src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs:33-34`):
  ```csharp
  form.Update(request.Title.Trim(), form.Description ?? string.Empty, form.IsPublic,
      form.ExpiresAt, form.ShowResultsAfterSubmit);
  ```
  `SaveFormEditorRequest` has no `IsPublic` field either, so there is currently
  no path in the app that lets a user flip a form's visibility after creation.
- The domain method `Form.Update()` already accepts and reassigns `isPublic`
  (`src/FormAI.Domain/Entities/Form.cs:40-47`), and `Form.IsPublic` is already
  surfaced read-only to the frontend today: `FormSummary.isPublic` is rendered
  as a "Public"/"Private" badge on the dashboard (`FormCard.tsx:27-30`), and
  `FormDetail.isPublic` is returned by `getForm()` but never read in
  `FormEditorPage.tsx`.
- There's no DB-level default on the `IsPublic` column — `FormConfiguration.cs`
  only has `.IsRequired()`, no `.HasDefaultValue(...)`
  (`src/FormAI.Infrastructure/Data/Configurations/FormConfiguration.cs:24-25`).
  So flipping the default is purely an application-layer change; **no EF Core
  migration is needed for this phase.**
- Unauthenticated/non-owner access to a private form already returns **404**
  (not 403) via `GetFormHandler`
  (`src/FormAI.Application/Forms/GetForm/GetFormHandler.cs:25-26` throws
  `NotFoundException`, mapped to 404 by
  `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs:36-37`). This
  phase doesn't change that behavior, just makes forms land in that state by
  default until the owner opts in to public.
- Separately, there's also no way for a user to set a form's **description**
  anywhere in the UI today, even though the whole backend/domain stack already
  supports it: `Form.Description` is a nullable `string`
  (`src/FormAI.Domain/Entities/Form.cs:9`), the DB column is nullable with a
  1024-char max and no migration is needed to change it
  (`src/FormAI.Infrastructure/Data/Configurations/FormConfiguration.cs:17-19`),
  and `GetFormResponse`/`FormDetail` already carry `description` through to
  the frontend (`GetFormResponse.cs:8`, `GetFormHandler.cs:28`,
  `frontend/src/types/form.ts:34`). What's missing is purely the request-side
  plumbing and UI:
  - `GenerateFormRequest` has no `Description` field, and
    `GenerateFormHandler.cs:49` hardcodes `description: null` when creating
    the form.
  - `SaveFormEditorRequest` has no `Description` field either, and
    `SaveFormEditorHandler.cs:33` passes the form's **existing**
    `form.Description ?? string.Empty` straight back — same "can't actually
    change it" gap as `IsPublic` had before Part B.
  - Neither `CreateFormPage.tsx` nor `FormEditorPage.tsx` render a description
    input.

**Requirements for this phase:**
1. Newly generated forms default to **private** (`isPublic: false`) instead of
   public.
2. The form editor (`/forms/:id/edit`) gets a "Public" checkbox that reflects
   and updates the form's current visibility, saved together with the title
   and questions through the existing single-transaction `SaveFormEditor` flow
   (same reasoning as Phase 9's combined save — don't add a second endpoint
   call that could partially fail).
3. The "create a new form" popup (`CreateFormPage.tsx`) gets an optional
   description field, sent through form generation.
4. The form editor gets an editable description field, saved together with
   title/isPublic/questions through the same single `SaveFormEditor` request.

---

## Part A — flip the creation default to private

### A1. `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs:52` — change the hardcoded value

```csharp
var form = Form.Create(
    title: title,
    description: null,
    createdBy: requestingUserId,
    sourceType: SourceType.Text,
    isPublic: false,
    expiresAt: DateTime.UtcNow.AddDays(15),
    showResultsAfterSubmit: false
);
```

Just the one literal, `true` → `false`. `GenerateFormRequest` is left alone —
this phase doesn't add a way to request a public form *at creation time*; the
user makes it public afterward from the editor (Part B). If a future phase
wants a "make public on generate" toggle on `CreateFormPage`, that's a
separate, additive change to `GenerateFormRequest`/`GenerateFormHandler`.

No changes needed to `CreateFormRequest`/`CreateFormHandler`
(`src/FormAI.Application/Forms/CreateForm/`) — `IsPublic` there is already a
required, caller-supplied field with no hardcoded default, and that endpoint
isn't used by the frontend today.

---

## Part B — editable visibility in the form editor

### B1. `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs` — add `IsPublic`

```csharp
public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    bool IsPublic,
    List<QuestionInput> Questions
);
```

### B2. `SaveFormEditorHandler.cs:33-34` — pass the requested value through

```csharp
form.Update(request.Title.Trim(), form.Description ?? string.Empty, request.IsPublic,
    form.ExpiresAt, form.ShowResultsAfterSubmit);
```

No validation needed beyond the existing title checks — `IsPublic` is a plain
`bool`, every value is valid.

### B3. `src/FormAI.API/Controllers/FormsController.cs` — no changes

`SaveEditor` already binds the full request body via
`[FromBody] SaveFormEditorRequest request`, so the new `IsPublic` field is
picked up automatically once it exists on the record. No controller edit
required.

### B4. `frontend/src/api/forms.ts` — add `isPublic` to the editor payload

```ts
export interface SaveFormEditorPayload {
    title: string;
    isPublic: boolean;
    questions: FormQuestion[];
}

export async function saveFormEditor(formId: string, payload: SaveFormEditorPayload): Promise<void> {
    await api.put(`/forms/${formId}/editor`, {
        title: payload.title,
        isPublic: payload.isPublic,
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

### B5. `frontend/src/pages/FormEditorPage.tsx` — checkbox state + wiring

Add `isPublic` state next to the existing `title` state
(`FormEditorPage.tsx:25-26`), seeded from the loaded form in the same
`useEffect` that seeds `title` (`FormEditorPage.tsx:32-38`):

```tsx
const [isPublic, setIsPublic] = useState(false);
// ...
getForm(id).then(data => {
    setForm(data);
    setQuestions(data.questions);
    setTitle(data.title);
    setIsPublic(data.isPublic);
    setState('ready');
}).catch(() => setState('error'));
```

Add the checkbox in the sticky header, next to the question count under the
title input (`FormEditorPage.tsx:110-127`):

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
    <div className="flex items-center gap-3 mt-1">
        <p className="text-xs text-gray-400">
            {questions.length} question{questions.length !== 1 ? 's' : ''}
        </p>
        <label className="flex items-center gap-1.5 text-xs text-gray-500 cursor-pointer">
            <input
                type="checkbox"
                checked={isPublic}
                onChange={e => setIsPublic(e.target.checked)}
                className="h-3.5 w-3.5 rounded border-gray-300 text-brand-600 focus:ring-brand-400"
            />
            Public
        </label>
    </div>
</div>
```

Update `handleSave` to send `isPublic` and reflect it back into local `form`
state on success (mirrors how Phase 9 patched `title` in the same spot,
`FormEditorPage.tsx:79-83`):

```tsx
try {
    await saveFormEditor(id, { title: trimmedTitle, isPublic, questions });
    setForm(f => f ? { ...f, title: trimmedTitle, isPublic } : f);
    setState('ready');
}
catch {
    setSaveError('Failed to save. Please try again');
    setState('ready');
}
```

No changes needed to `frontend/src/types/form.ts` — `FormDetail.isPublic`
already exists (it's how `FormCard.tsx`'s public/private badge gets its data
today, just via a different fetch path). No changes needed to
`CreateFormPage.tsx` — visibility isn't set at creation time in this phase
(see Part A).

---

## Part C — description field on create/generate

### C1. `src/FormAI.Application/Forms/GenerateForm/GenerateFormRequest.cs` — add `Description`

```csharp
public record GenerateFormRequest(
    string? Title,
    string? Description,
    string SourceText,
    SourceType SourceType,
    string? SourceUrl,
    int QuestionCount,
    QuestionType[]? AllowedTypes,
    string DifficultyLevel,
    bool IncludeCorrectAnswers
);
```

Nullable/optional, matching `Title`'s convention — omitting it (or sending
whitespace-only) leaves the form with no description.

### C2. `GenerateFormHandler.cs:49` — use the provided description

```csharp
var description = string.IsNullOrWhiteSpace(request.Description)
    ? null
    : request.Description.Trim();

if (description?.Length > 1024)
    throw new ArgumentException("Description must be at most 1024 characters.");
```

```csharp
var form = Form.Create(
    title: title,
    description: description,
    createdBy: requestingUserId,
    sourceType: SourceType.Text,
    isPublic: false,
    expiresAt: DateTime.UtcNow.AddDays(15),
    showResultsAfterSubmit: false
);
```

Same `ArgumentException` pattern already used for the title-length and
source-text checks a few lines above, so it's caught by the same global
exception handling as those.

### C3. `frontend/src/api/forms.ts` — pass `description` through `generateForm`

```ts
export interface GenerateFormPayload {
    title?: string;
    description?: string;
    sourceText: string;
    questionCount: number;
    difficultyLevel: string;
    includeCorrectAnswers: boolean;
}

export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
    const response = await api.post<GenerateFormResult>('/forms/generate/text', {
        title: payload.title?.trim() || null,
        description: payload.description?.trim() || null,
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

### C4. `frontend/src/pages/CreateFormPage.tsx` — add the optional description field

Extend the schema (`CreateFormPage.tsx:10-17`):

```ts
const createFormSchema = z.object({
  title: z.string().max(255, "Title must be at most 255 characters").optional(),
  description: z.string().max(1024, "Description must be at most 1024 characters").optional(),
  sourceText: z.string().min(50, "Source text must be at least 50 characters long"),
  questionCount: z.coerce.number().int().min(1, "At least 1 question.").
    max(20, "At most 20 questions."),
  difficultyLevel: z.enum(["Easy", "Medium", "Hard"], "Difficulty level must be one of Easy, Medium, or Hard."),
  includeCorrectAnswers: z.boolean()
});
```

Add the field right after the title input (`CreateFormPage.tsx:80`, before
"Source content"):

```tsx
<div className="flex flex-col gap-1">
  <label htmlFor="description" className="text-sm font-medium text-gray-700">
    Description <span className="text-gray-400 font-normal">(optional)</span>
  </label>
  <textarea
    id="description"
    rows={2}
    maxLength={1024}
    className={
      'w-full rounded-lg border px-3 py-2 text-sm shadow-sm outline-none ' +
      'focus:ring-2 focus:ring-brand-500 focus:border-brand-500 ' +
      (errors.description ? 'border-red-400 focus:ring-red-400 ' : 'border-gray-300 ')
    }
    placeholder="A short note about what this form is for…"
    {...register('description')}
  />
  {errors.description && (
    <span className="text-xs text-red-500">{errors.description.message}</span>
  )}
</div>
```

No change needed to `defaultValues` — `description` simply starts undefined,
same as `title`.

---

## Part D — editable description in the form editor

### D1. `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs` — add `Description`

```csharp
public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    string? Description,
    bool IsPublic,
    List<QuestionInput> Questions
);
```

### D2. `SaveFormEditorHandler.cs` — validate and pass the requested description through

```csharp
if (request.Description?.Length > 1024)
    throw new ArgumentException("Description must be at most 1024 characters.");

form.Update(request.Title.Trim(), request.Description?.Trim() ?? string.Empty, request.IsPublic,
    form.ExpiresAt, form.ShowResultsAfterSubmit);
```

(Same spot as the Part B change to this call — both `IsPublic` and
`Description` now come from the request instead of being read back from the
existing `form`.)

### D3. `frontend/src/api/forms.ts` — add `description` to the editor payload

```ts
export interface SaveFormEditorPayload {
    title: string;
    description: string;
    isPublic: boolean;
    questions: FormQuestion[];
}

export async function saveFormEditor(formId: string, payload: SaveFormEditorPayload): Promise<void> {
    await api.put(`/forms/${formId}/editor`, {
        title: payload.title,
        description: payload.description,
        isPublic: payload.isPublic,
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

### D4. `frontend/src/pages/FormEditorPage.tsx` — description state + textarea

Add `description` state next to `title`/`isPublic` (`FormEditorPage.tsx:25-26`),
seeded from the loaded form:

```tsx
const [description, setDescription] = useState('');
const [descriptionError, setDescriptionError] = useState('');
// ...
getForm(id).then(data => {
    setForm(data);
    setQuestions(data.questions);
    setTitle(data.title);
    setDescription(data.description ?? '');
    setIsPublic(data.isPublic);
    setState('ready');
}).catch(() => setState('error'));
```

Add a textarea below the title/visibility row in the sticky header
(`FormEditorPage.tsx:110-127`) — since the header is already fairly dense
once the "Public" checkbox from Part B is added, put the description on its
own line below the title so it doesn't crowd the row:

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
    <textarea
        value={description}
        maxLength={1024}
        rows={1}
        onChange={e => { setDescription(e.target.value); setDescriptionError(''); }}
        placeholder="Add a description…"
        className={
            'block w-full max-w-xs resize-none text-xs text-gray-500 bg-transparent ' +
            'border border-transparent rounded px-1 -mx-1 outline-none ' +
            'hover:border-gray-200 focus:border-brand-400 focus:ring-1 focus:ring-brand-400 ' +
            (descriptionError ? 'border-red-400' : '')
        }
    />
    {descriptionError && <p className="text-xs text-red-500 px-1">{descriptionError}</p>}
    <div className="flex items-center gap-3 mt-1">
        <p className="text-xs text-gray-400">
            {questions.length} question{questions.length !== 1 ? 's' : ''}
        </p>
        <label className="flex items-center gap-1.5 text-xs text-gray-500 cursor-pointer">
            <input
                type="checkbox"
                checked={isPublic}
                onChange={e => setIsPublic(e.target.checked)}
                className="h-3.5 w-3.5 rounded border-gray-300 text-brand-600 focus:ring-brand-400"
            />
            Public
        </label>
    </div>
</div>
```

Update `handleSave` to validate and send `description` alongside the existing
fields:

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

    const trimmedDescription = description.trim();
    if (trimmedDescription.length > 1024) {
        setDescriptionError('Description must be at most 1024 characters.');
        return;
    }

    setSaveError('');
    setState('saving');

    try {
        await saveFormEditor(id, { title: trimmedTitle, description: trimmedDescription, isPublic, questions });
        setForm(f => f ? { ...f, title: trimmedTitle, description: trimmedDescription, isPublic } : f);
        setState('ready');
    }
    catch {
        setSaveError('Failed to save. Please try again');
        setState('ready');
    }
}
```

---

## Verification

- Backend: `dotnet build FormAI.sln`.
  - `POST /api/forms/generate/text` → the created form's `isPublic` is now
    `false` (check via `GET /api/forms/{id}` as the owner, or query the DB
    directly).
  - As an unauthenticated user (or a different logged-in user), `GET
    /api/forms/{id}` for that newly generated form → `404` (private-by-default
    now blocks access where it previously wouldn't have).
  - `PUT /api/forms/{id}/editor` with `isPublic: true` (owner) → `204`, then
    `GET /api/forms/{id}` as an unauthenticated user → `200` (form is now
    reachable).
  - `PUT /api/forms/{id}/editor` with `isPublic: false` on a form that was
    public → `204`, then unauthenticated `GET /api/forms/{id}` → back to `404`.
  - `PUT /api/forms/{id}` (`UpdateForm`, unrelated to this phase) still accepts
    and applies `isPublic` as before — unaffected.
  - `POST /api/forms/generate/text` with `description: "some notes"` → created
    form's `description` is `"some notes"`. With `description: null` or
    omitted → `description` is `null`, same as today's behavior.
  - `POST /api/forms/generate/text` with a 1025-char `description` → `400`.
  - `PUT /api/forms/{id}/editor` with a `description` (owner) → `204`, and
    `GET /api/forms/{id}` reflects the new description.
  - `PUT /api/forms/{id}/editor` with a 1025-char `description` → `400`, and
    confirm title/isPublic/questions were *not* updated either (same
    single-transaction rollback guarantee as Phase 9's title validation).
- Frontend: run the dev server, log in.
  - Generate a new form → open it in the editor → the "Public" checkbox is
    **unchecked** by default, and the dashboard's `FormCard` badge for that
    form shows "Private".
  - Check the box, click Save → reload the page (or navigate away and back) →
    checkbox stays checked, dashboard badge flips to "Public".
  - Uncheck it, click Save → reload → checkbox stays unchecked, dashboard
    badge flips back to "Private".
  - Confirm title editing and question edits (add/remove/reorder) still save
    correctly together with the visibility change in the same request (check
    the network tab: one `PUT /forms/{id}/editor` call carrying `title`,
    `description`, `isPublic`, and `questions` together).
  - On `/forms/new`: leave description blank, submit → generated form has no
    description. Enter a description → it's used verbatim and shows up when
    the form is opened in the editor. Enter >1024 chars → inline validation
    error, no request sent.
  - On `/forms/:id/edit`: edit the description, click Save → reload → the
    description persists. Clear it entirely and click Save → persists as
    empty (no error — description is optional, unlike title).
