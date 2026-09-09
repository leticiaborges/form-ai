# Phase 25 — Editable expiry in the form editor

## Context

Phase 24 lets the owner pick an expiry date when **creating** a form. Phase 25 lets them **edit it** after
creation, from the Edit form tab.

The piece already built — `DateTimeInput` — is reused here. The work is:

- **Frontend:** add expiry state to `FormEditorPage`, pass it to `FormEditorTab`, render `DateTimeInput`,
  wire it into the save flow.
- **Backend:** accept `ExpiresAt` in `SaveFormEditorRequest`, validate it, call `form.Update()` with it.
- **Docs:** expiry is no longer creation-only.

### Why not show expiry elsewhere

The Results tab, the dashboard form card, and the published form page all could display when a form
expires. That is out of scope here — phase 24's "Displaying an expiry" in the "After this phase" section
covers the reverse conversion (API returns UTC, render as local time) that all three would need. Editing
is the blocker removed by this phase.

### The domain already supports this

`Form.Update()` has an `expiresAt` parameter and calls `ExpiresAt = expiresAt;`. The only thing blocking
the editor is the absence of a request field and a UI to fill it. Everything else is built.

### What is already implemented

Nothing yet — `SaveFormEditorRequest` has no `ExpiresAt` field, `FormEditorTab` doesn't pass expiry to
`FormEditorPage`, and `FormEditorPage` doesn't manage it.

---

## Files

| File | Change |
|---|---|
| `frontend/src/pages/FormEditorPage.tsx` | state, validation, save |
| `frontend/src/components/FormEditorTab.tsx` | props, render `DateTimeInput`, event handler |
| `frontend/src/api/forms.ts` | add `expiresAt` to payload and request body |
| `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs` | add `DateTime ExpiresAt` |
| `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorHandler.cs` | validate and use it |
| `docs/known-gaps.md`, `CLAUDE.md` | expiry is now fully editable |

---

## Step 1 — `FormEditorPage.tsx` — add state and pass to child

**1a. Add state for expiry at the top, with title/description/isGraded:**

Find this block around line 40:

```tsx
const [description, setDescription] = useState('');
const [descriptionError, setDescriptionError] = useState('');

const [showDeleteModal, setShowDeleteModal] = useState(false);
```

Add below it:

```tsx
const [expiresAt, setExpiresAt] = useState('');
const [expiresAtError, setExpiresAtError] = useState('');
```

**1b. Initialize expiresAt when the form loads:**

Find the `setFormData` function around line 52:

```tsx
function setFormData(data: FormDetail) {
    setForm(data);
    setQuestions(data.questions);
    setTitle(data.title);
    setDescription(data.description ?? '');
    setIsPublic(data.isPublic);
    setIsGraded(data.isGraded);
}
```

Add this line at the end:

```tsx
    setExpiresAt(data.expiresAt ?? '');
```

**1c. Pass expiresAt and its handlers to `FormEditorTab`:**

Find the `<FormEditorTab` element around line 218 and add these props:

```tsx
                    <FormEditorTab
                      form={form}
                      questions={questions}
                      onQuestionsChange={setQuestions}
                      description={description}
                      onDescriptionChange={setDescription}
                      descriptionError={descriptionError}
                      isPublic={isPublic}
                      onIsPublicChange={setIsPublic}
                      isGraded={isGraded}
                      onIsGradedChange={toggleGraded}
                      expiresAt={expiresAt}
                      onExpiresAtChange={setExpiresAt}
                      expiresAtError={expiresAtError}
                    />
```

**1d. Validate expiresAt in `handleSave`:**

Find the `async function handleSave()` block around line 100. Before the `setState('saving')` line, add:

```tsx
        // Clear old error
        setExpiresAtError('');

        if (!expiresAt) {
            setExpiresAtError('Pick an expiry date and time.');
            return;
        }

        if (Number.isNaN(Date.parse(expiresAt))) {
            setExpiresAtError('Enter a valid date and time.');
            return;
        }

        if (new Date(expiresAt) <= new Date()) {
            setExpiresAtError('The expiry must be in the future.');
            return;
        }
```

This mirrors the validation from `CreateFormPage`, so it's the same rule applied twice — once at create,
once at edit.

**1e. Pass expiresAt to the save call:**

Find the `saveFormEditor` call inside `handleSave` and add `expiresAt`:

```tsx
            await saveFormEditor(id, {
                title: trimmedTitle,
                description: trimmedDescription,
                isPublic,
                isGraded,
                expiresAt: new Date(expiresAt),
                questions
            });
```

Note: convert to `Date` exactly like step 3e in phase 24. The API will call `.toISOString()` to make the
UTC version.

---

## Step 2 — `FormEditorTab.tsx` — accept and render expiry

**2a. Add props to the interface:**

Find `FormEditorTabProps` interface at the top, around line 11:

```tsx
interface FormEditorTabProps {
  form: FormDetail;
  questions: FormQuestion[];
  onQuestionsChange: (questions: FormQuestion[]) => void;
  description: string;
  onDescriptionChange: (value: string) => void;
  descriptionError: string;
  isPublic: boolean;
  onIsPublicChange: (value: boolean) => void;
  isGraded: boolean;
  onIsGradedChange: (value: boolean) => void;
}
```

Add after `onIsGradedChange`:

```tsx
  expiresAt: string;
  onExpiresAtChange: (value: string) => void;
  expiresAtError: string;
```

**2b. Add props to the destructuring:**

Find the component function signature around line 24, and add to the destructuring:

```tsx
export function FormEditorTab({
  form,
  questions,
  onQuestionsChange,
  description,
  onDescriptionChange,
  descriptionError,
  isPublic,
  onIsPublicChange,
  isGraded,
  onIsGradedChange,
  expiresAt,
  onExpiresAtChange,
  expiresAtError
}: Readonly<FormEditorTabProps>) {
```

**2c. Render `DateTimeInput` below the checkbox row:**

Find the closing `</div>` of the checkbox/link section around line 140. Right after it (still inside the
outer flex container), add:

```tsx
      <div className="flex items-center justify-between">
        <DateTimeInput
          id="expiresAt"
          label="Expires at"
          value={expiresAt}
          onChange={onExpiresAtChange}
          error={expiresAtError}
        />
      </div>
```

This renders the date/time boxes with the error below.

---

## Step 3 — `frontend/src/api/forms.ts` — wire the payload

**3a. Add `expiresAt` to `SaveFormEditorPayload`:**

Find the interface around line 18:

```tsx
export interface SaveFormEditorPayload {
    title: string;
    description: string;
    isPublic: boolean;
    isGraded: boolean;
    questions: FormQuestion[];
}
```

Add after `isGraded`:

```tsx
    expiresAt: Date;
```

**3b. Add `expiresAt` to the request body:**

Find the `saveFormEditor` function around line 26, and in the request object being sent, add after
`isGraded`:

```tsx
export async function saveFormEditor(formId: string, payload: SaveFormEditorPayload): Promise<void> {
    await api.put(`/forms/${formId}/editor`, {
        title: payload.title,
        description: payload.description?.trim() || null,
        isPublic: payload.isPublic,
        isGraded: payload.isGraded,
        expiresAt: payload.expiresAt.toISOString(),
        questions: payload.questions.map(...)
```

Exactly like phase 24: `.toISOString()` converts local wall-clock to UTC with the trailing `Z`.

---

## Step 4 — `SaveFormEditorRequest.cs` — add the field

**4a. Add `DateTime ExpiresAt` to the record:**

Open `src/FormAI.Application/Forms/SaveFormEditor/SaveFormEditorRequest.cs` and modify the record:

```csharp
public record SaveFormEditorRequest(
    Guid FormId,
    Guid RequestingUserId,
    string Title,
    string? Description,
    bool IsPublic,
    bool IsGraded,
    DateTime ExpiresAt,
    List<QuestionInput> Questions
);
```

Add it after `IsGraded`. The order doesn't matter functionally, but putting it here keeps form-level
fields together before questions.

---

## Step 5 — `SaveFormEditorHandler.cs` — validate and call Update

**5a. Validate the expiry:**

Find the validation block inside `HandleAsync`, around line 50. After the description length check
(around line 46), add:

```csharp
        if (request.ExpiresAt <= DateTime.UtcNow)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["expiresAt"] = ["The expiry must be in the future."]
            });
```

Same check as phase 24 — the UI validates client-side, and the server validates to enforce the rule.

**5b. Change the `form.Update()` call to use `request.ExpiresAt`:**

Find line 63–66, the `form.Update()` call:

```csharp
form.Update(request.Title.Trim(),
 request.Description?.Trim() ?? string.Empty,
  request.IsPublic,
    form.ExpiresAt, form.ShowResultsAfterSubmit, request.IsGraded);
```

Change the fourth parameter from `form.ExpiresAt` to `request.ExpiresAt`:

```csharp
form.Update(request.Title.Trim(),
 request.Description?.Trim() ?? string.Empty,
  request.IsPublic,
    request.ExpiresAt, form.ShowResultsAfterSubmit, request.IsGraded);
```

**Why:** Currently, the handler is **ignoring** any expiry the user picks and keeping whatever was
stored before. This is the line that applies user edits to the form entity. Swapping `form.ExpiresAt` for
`request.ExpiresAt` makes this line actually use the value from the editor.

The validation you added in 5a runs *before* this line, so if the user sends a past date, the handler
throws and never reaches `form.Update()`. If it passes validation, line 66 applies it to the entity.

---

## Step 6 — Docs

**6a. `docs/known-gaps.md`** — the **"Editing the expiry date"** row now needs narrowing. Currently it
says the owner can't edit the expiry and forms get a fixed 15-day expiry. Replace it with:

```markdown
| **Never-expiring forms** | The owner must pick an expiry when generating a form and can edit it afterward, but the UI has no way to express "no expiry" — `SaveFormEditorRequest.ExpiresAt` is non-nullable. The domain allows it via `Form.ExpiresAt` being nullable, and forms without an expiry could be created via the API. |
```

**6b. `CLAUDE.md`, "Business rules"** — under *Forms and questions*, the row about generated forms still
says "expiry **15 days out**". Update it to:

```markdown
- Generated forms are created **private**, with `ShowResultsAfterSubmit = false` and the expiry the owner
  picked. The expiry is required and must be in the future; it arrives as UTC and is compared against
  `DateTime.UtcNow`. The owner can change it at any time via the editor.
```

**6c. `CLAUDE.md`, "Known gaps" headline** — remove *"editable expiry"* from the list of things not
built.

---

## Verification

### 1. Build

```bash
cd frontend && npm run build
dotnet build FormAI.sln
```

Likeliest breaks:
- Step 4: if `SaveFormEditorRequest` signature changed and something still constructs the old form, you'll
  get a "missing argument" compiler error.
- Step 2c: if the import of `DateTimeInput` is missing, TypeScript will complain.

### 2. Load an existing form

Open the editor on a form you made in phase 24. The **Expires at** box should show the date and time
you picked at creation.

### 3. Change the expiry

- Pick a different date and time.
- Edit the title too, so you're changing multiple fields at once — this is how you know the save path
  applied all of them.
- Hit **Save**.
- Refresh the page. Both title and expiry should persist.

### 4. Validation — client side

- Clear both date and time boxes and click Save → *"Pick an expiry date and time."*
- Pick today's date and hit Save → *"The expiry must be in the future."*
- Both messages should appear under the two boxes, borders red.

### 5. Validation — server side

With the API running, bypass the browser and send a past expiry:

```bash
curl -i -X PUT http://localhost:5155/api/forms/<formId>/editor \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"title":"Test","description":"","isPublic":false,"isGraded":false,"expiresAt":"2020-01-01T00:00:00.000Z","questions":[...]}'
```

Expect **400** with `"One or more validation errors occurred."` and `errors.expiresAt`. A **500** means
step 5a was not applied.

### 6. The expiry actually stops submissions

Generate a form expiring two minutes from now. Publish it. In a private window, answer and submit — it
should work. Wait for expiry, then try again: *"This form is no longer accepting submissions."* The
owner can still edit it.

### 7. Full regression

```bash
dotnet test FormAI.sln
cd frontend && npm run build
```

---

## After this phase

- **Showing expiry to anyone** — Results tab, dashboard, respondent view. All need the UTC → local
  conversion, the reverse of what `.toISOString()` does. A single helper in `frontend/src/utils/` would
  serve them all.
- **Never-expiring forms** — a nullable request field and a "no expiry" checkbox would let the owner opt
  out. The domain supports it.
- **The three duplicate input styles** — `DateInput`, `TimeInput`, `Input` all have the same Tailwind
  string. The next pass on inputs would factor it out.
