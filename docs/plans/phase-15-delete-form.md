# Phase 15 — Delete a form from the editor (with confirmation)

## Context

A form owner can create, edit, close and share a form, but there is **no way to
delete one**. `FormEditorPage`'s header holds only *Back* and *Save*;
`DashboardPage`/`FormCard` have no per-card actions at all. A form created by
mistake, or a finished quiz whose responses are no longer wanted, stays in the
dashboard forever.

Deleting a form is destructive in a way nothing else in this app is: it takes
the questions, their options, **every submission** and every answer with it. So
the feature is two things, not one — an API call, and a confirmation step that
tells the user exactly what is about to disappear before it does.

### What already exists (do not rebuild it)

The **entire backend is already implemented and wired**. Verified:

| Piece | Location | Status |
|---|---|---|
| `DELETE /api/forms/{id}` | `src/FormAI.API/Controllers/FormsController.cs:103-109` | exists, `[Authorize]` inherited from the controller, returns `204 NoContent` |
| `DeleteFormHandler` | `src/FormAI.Application/Forms/DeleteForm/DeleteFormHandler.cs` | exists — `NotFoundException` when missing, `ForbiddenException` when `form.CreatedBy != request.RequestingUserId` |
| `DeleteFormRequest` | `src/FormAI.Application/Forms/DeleteForm/DeleteFormRequest.cs` | exists |
| `IFormRepository.DeleteAsync` | `src/FormAI.Application/Interfaces/IFormRepository.cs:12` | exists |
| `FormRepository.DeleteAsync` | `src/FormAI.Infrastructure/Repositories/FormRepository.cs:51-60` | exists — `Forms.Remove(form)` + `SaveChangesAsync` |
| DI registration | `src/FormAI.Infrastructure/DependencyInjection.cs` | exists |
| Error → HTTP mapping | `src/FormAI.API/Middleware/ExceptionHandlingMiddleware.cs` | `NotFoundException → 404`, `ForbiddenException → 403`, body `{ message, errors }` |

And the cascade the user asked for is **already enforced at the database level**
— not just by EF, but by real PostgreSQL `ON DELETE CASCADE` constraints created
in `20260421150052_InitialCreate.cs`:

```
forms
├── form_source_contents                    cascade   (FormConfiguration.cs:46-49)
├── form_questions                          cascade   (FormConfiguration.cs:36-39)
│   ├── question_options                    cascade   (QuestionOptionConfiguration.cs:19-22)
│   └── answers        [via question_id]    cascade   (AnswerConfiguration.cs:28-31)
└── submissions                             cascade   (FormConfiguration.cs:41-44)
    └── answers        [via submission_id]  cascade   (AnswerConfiguration.cs:23-26)
        └── answer_selected_options         cascade   (AnswerConfiguration.cs:33-36)
```

`answers` is reachable by two cascade paths (question *and* submission).
PostgreSQL allows that — unlike SQL Server — so a single `DELETE FROM forms`
resolves cleanly. Phase 13 removed the last `RESTRICT` FK
(`answer_selected_options → question_options`), so nothing blocks the cascade
any more.

`FormRepository.DeleteAsync` loads only the `forms` row and lets the database do
the rest, so **no new migration and no schema change is needed in this phase.**

### Scope decisions (already made — don't re-litigate while implementing)

- **The delete button lives on `FormEditorPage` only.** `DashboardPage` /
  `FormCard` are explicitly out of scope for this phase.
- **Hard delete, not soft delete.** The rows are physically removed. `Form`
  already offers the soft alternative — `Form.Close()` sets `ExpiresAt = UtcNow`
  — and this feature is deliberately the other thing.
- **Two components, not one:** a *generic* `ConfirmModal` and a *delete-specific*
  `DeleteFormModal` that composes it. The generic one owns the shell (backdrop,
  card, title, footer buttons, loading state); the specific one owns the copy
  and the counts. The next destructive action reuses `ConfirmModal` and writes
  only its own body.
- **The dialog names the form and shows counts.** "Are you sure?" with no
  numbers is not enough weight for an action that erases response data. No
  type-the-title-to-confirm step — the counts carry the warning.
- **The submission count comes from its own endpoint, fetched only when the
  Delete button is clicked.** It is *not* bolted onto `GET /api/forms/{id}`.
  That endpoint is `[AllowAnonymous]` and serves the public answer page on every
  respondent's page load; making it count submissions would tax the hot read
  path to feed a dialog most visitors will never open. The count also goes stale
  the moment someone submits, so fetching it at click time is more accurate than
  fetching it at page load.
- **Backend delete path is left exactly as-is.** The known rough edges
  (`DeleteFormHandler`'s `public readonly` field, its discarded `bool` return,
  the redundant second `FindAsync` in `DeleteAsync`) are real but pre-existing
  and out of scope. Part A adds a *new, separate* read endpoint; it does not
  touch the delete path.

---

## Part A — A dedicated submission-count endpoint (backend)

The dialog says "*23 submissions and all answers*". `questions.length` is
already in the editor's React state, but the submission count is not — and it is
deliberately **not** added to `GetFormResponse` (see the scope decisions). It
gets its own owner-only endpoint, called once, when the user clicks Delete:

```
GET /api/forms/{id}/submissions/count   →  200 { "submissionCount": 23 }
```

Five small pieces, following the existing folder-per-use-case convention.

### A1. `src/FormAI.Application/Interfaces/ISubmissionRepository.cs`

Add one method to the existing interface:

```csharp
Task<int> CountByFormAsync(Guid formId, CancellationToken cancellationToken = default);
```

> Heads-up while editing this file: `ISubmissionRepository.cs` currently declares
> **no namespace** — it sits in the global namespace even though
> `SubmissionRepository.cs` has a (redundant) `using FormAI.Application.Interfaces;`.
> Leave that as it is; fixing it is a separate, wider change.

### A2. `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs`

```csharp
public async Task<int> CountByFormAsync(Guid formId, CancellationToken cancellationToken = default)
{
    return await _context.Submissions
        .CountAsync(s => s.FormId == formId, cancellationToken);
}
```

A plain `SELECT count(*)` — no rows materialised.

### A3. New folder `src/FormAI.Application/Forms/GetSubmissionCount/`

Two files, matching how `DeleteForm/` is laid out.

`GetSubmissionCountRequest.cs`:

```csharp
namespace FormAI.Application.Forms.GetSubmissionCount;

public record GetSubmissionCountRequest(Guid FormId, Guid RequestingUserId);

public record GetSubmissionCountResponse(int SubmissionCount);
```

`GetSubmissionCountHandler.cs`:

```csharp
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;

namespace FormAI.Application.Forms.GetSubmissionCount;

public class GetSubmissionCountHandler
{
    private readonly IFormRepository _formRepository;
    private readonly ISubmissionRepository _submissionRepository;

    public GetSubmissionCountHandler(IFormRepository formRepository,
        ISubmissionRepository submissionRepository)
    {
        _formRepository = formRepository;
        _submissionRepository = submissionRepository;
    }

    public async Task<GetSubmissionCountResponse> HandleAsync(
        GetSubmissionCountRequest request, CancellationToken cancellationToken)
    {
        var form = await _formRepository.GetByIdAsync(request.FormId, cancellationToken);
        if (form == null)
            throw new NotFoundException("Form not found");

        if (form.CreatedBy != request.RequestingUserId)
            throw new ForbiddenException("You do not own this form");

        var count = await _submissionRepository.CountByFormAsync(request.FormId, cancellationToken);

        return new GetSubmissionCountResponse(count);
    }
}
```

The owner check is the same four lines used by `DeleteFormHandler`,
`CloseFormHandler` and `UpdateFormHandler` — copy the idiom verbatim rather than
inventing a variant. It matters here: how many people answered a form is the
owner's business, not a respondent's.

Returning a `record` rather than a bare `int` keeps the response a JSON object
(`{ "submissionCount": 23 }`), so the endpoint can grow a second field later
without breaking clients.

### A4. `src/FormAI.API/Controllers/FormsController.cs`

Inject the handler alongside the seven existing ones (add the field, the
constructor parameter and the assignment), then add the action next to
`Delete`. The controller's class-level `[Authorize]` covers it — which is
exactly why this endpoint lives here and **not** in `SubmissionsController`,
whose actions are all anonymous:

```csharp
// GET /api/forms/{id}/submissions/count
[HttpGet("{id:guid}/submissions/count")]
public async Task<IActionResult> GetSubmissionCount(Guid id, CancellationToken cancellationToken)
{
    var response = await _getSubmissionCount.HandleAsync(
        new GetSubmissionCountRequest(id, CurrentUserId), cancellationToken);

    return Ok(response);
}
```

### A5. `src/FormAI.Infrastructure/DependencyInjection.cs`

Register the handler with the others (around line 60-70):

```csharp
services.AddScoped<GetSubmissionCountHandler>();
```

`ISubmissionRepository → SubmissionRepository` is already registered at line 41;
nothing else to wire.

---

## Part B — Two new calls in the frontend API layer

### B1. `frontend/src/api/forms.ts`

Append both, matching the existing style — plain exported `async function` with
an explicit return type, using the default-imported `api`:

```ts
export async function deleteForm(formId: string): Promise<void> {
    await api.delete(`/forms/${formId}`);
}

export async function getSubmissionCount(formId: string): Promise<number> {
    const response = await api.get<{ submissionCount: number }>(
        `/forms/${formId}/submissions/count`);

    return response.data.submissionCount;
}
```

`getSubmissionCount` unwraps to a bare `number` at the boundary so the page
never handles the envelope shape.

The shared `api` instance (`frontend/src/api/axios.ts`) already attaches the JWT
via its request interceptor and hard-redirects to `/login` on 401, so `403` and
`404` are the only failures these calls have to surface — both arrive as
`{ message }` and are read by `getErrorMessage`.

**`frontend/src/types/form.ts` needs no change.** `FormDetail` stays exactly as
it is — the count is not part of the form payload.

---

## Part C — A `danger` variant on the shared `Button`

`frontend/src/components/Button.tsx` has only `primary` and `outline`. A red
delete button could be faked with a `className` override (the component appends
`className` last), but two of them are needed — the header button and the
dialog's confirm button — and a destructive style belongs in the design system,
not copy-pasted.

Note the **focus ring moves out of `base` and into each variant**. Leaving
`focus-visible:ring-brand-500` in `base` and adding `focus-visible:ring-red-500`
in the variant string would leave the winner up to Tailwind's generated CSS
order, not the string order. Splitting it makes each variant unambiguous and
leaves `primary`/`outline` rendering exactly as they do today:

```ts
interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'outline' | 'danger';
  isLoading?: boolean,
}

const base =
  'inline-flex items-center justify-center rounded-lg px-4 py-1.5 text-sm font-semibold ' +
  'transition-colors focus-visible:outline-none focus-visible:ring-2 ' +
  'disabled:opacity-50 disabled:cursor-not-allowed';

const variants = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700 active:bg-brand-800 focus-visible:ring-brand-500',
  outline: 'border border-brand-600 text-brand-600 bg-transparent hover:bg-brand-50 focus-visible:ring-brand-500',
  danger:  'bg-red-600 text-white hover:bg-red-700 active:bg-red-800 focus-visible:ring-red-500',
};
```

Nothing else in the file changes — `disabled={disabled || isLoading}` and the
spinner branch stay as they are.

---

## Part D — `ConfirmModal` (the generic one)

### D1. New file `frontend/src/components/ConfirmModal.tsx`

There is no `Modal`/`Dialog`/`ConfirmDialog` anywhere in the app and no UI
library (`package.json` has no radix / headlessui / shadcn — every component is
hand-rolled Tailwind v4). The shell below is lifted from
`frontend/src/components/editors/AddQuestionModal.tsx:82-144`, which is the only
existing modal, so the two look identical on screen.

It sits in `components/` (not `components/editors/`) because it is not
editor-specific.

```tsx
import { useEffect } from "react";
import type { ReactNode } from "react";
import { Button } from "./Button";

interface ConfirmModalProps {
  title: string;
  children: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'primary' | 'danger';
  isLoading?: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

export function ConfirmModal({
  title,
  children,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'primary',
  isLoading = false,
  onConfirm,
  onClose,
}: Readonly<ConfirmModalProps>) {

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape' && !isLoading)
        onClose();
    }

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isLoading, onClose]);

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      onClick={() => { if (!isLoading) onClose(); }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-modal-title"
        className="w-full max-w-md rounded-2xl bg-white p-6 shadow-xl"
        onClick={e => e.stopPropagation()}
      >
        <h2 id="confirm-modal-title" className="mb-3 text-lg font-semibold text-gray-900">
          {title}
        </h2>

        <div className="text-sm text-gray-600">{children}</div>

        <div className="mt-6 flex justify-end gap-3">
          <Button variant="outline" onClick={onClose} disabled={isLoading}>
            {cancelLabel}
          </Button>
          <Button variant={variant} onClick={onConfirm} isLoading={isLoading}>
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  );
}
```

Design notes:

- **The body is `children`, not a `message: string` prop.** The delete copy needs
  a bolded form title and a bulleted list; a plain string prop would have forced
  either `dangerouslySetInnerHTML` or a second prop for every future variation.
- **Escape and backdrop-click close the dialog, but only when `!isLoading`.**
  Once the delete request is in flight, dismissing the dialog would leave the
  user staring at an unchanged page while the form is being deleted underneath
  them. `stopPropagation` on the card keeps clicks inside from closing it.
- **`isLoading` drives both buttons**: the confirm button shows the spinner,
  Cancel goes `disabled`. `Button` already handles `disabled || isLoading`.
- **No portal and no focus trap**, matching `AddQuestionModal`. Adding either is
  a fine follow-up but is not what this phase is about — don't expand it here.

---

## Part E — `DeleteFormModal` (the specific one)

### E1. New file `frontend/src/components/editors/DeleteFormModal.tsx`

Lives next to `AddQuestionModal.tsx` — it is an editor-page dialog. It renders
**no shell of its own**; it is copy plus counts wrapped around `ConfirmModal`.

`submissionCount` is `number | null` because the modal opens *immediately* on
click and the count arrives a moment later (Part F). `null` means "not known
yet, or the count request failed" and falls back to wording that is true either
way.

```tsx
import { ConfirmModal } from "../ConfirmModal";

interface DeleteFormModalProps {
  formTitle: string;
  questionCount: number;
  submissionCount: number | null;
  isDeleting: boolean;
  onConfirm: () => void;
  onClose: () => void;
}

function plural(count: number, noun: string) {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

export function DeleteFormModal({
  formTitle,
  questionCount,
  submissionCount,
  isDeleting,
  onConfirm,
  onClose,
}: Readonly<DeleteFormModalProps>) {
  return (
    <ConfirmModal
      title="Delete this form?"
      confirmLabel="Delete form"
      variant="danger"
      isLoading={isDeleting}
      onConfirm={onConfirm}
      onClose={onClose}
    >
      <p>
        <span className="font-medium text-gray-900">“{formTitle}”</span> and everything
        attached to it will be permanently deleted:
      </p>

      <ul className="mt-3 list-disc space-y-1 pl-5">
        <li>{plural(questionCount, 'question')} and their options</li>
        <li>
          {submissionCount === null
            ? 'every submission received so far, and all answers'
            : `${plural(submissionCount, 'submission')} and all answers`}
        </li>
      </ul>

      <p className="mt-3 font-medium text-red-600">This cannot be undone.</p>
    </ConfirmModal>
  );
}
```

Note there is **no spinner and no disabled confirm button** while the count
loads. The count is informational; the warning stands on its own without it, and
blocking a delete on a secondary read would be worse than showing slightly
vaguer copy for a few hundred milliseconds.

---

## Part F — Wire it into `FormEditorPage`

`frontend/src/pages/FormEditorPage.tsx`. Six edits.

### F1. Imports

```tsx
import { getForm, saveFormEditor, deleteForm, getSubmissionCount } from '../api/forms';
import { DeleteFormModal } from "../components/editors/DeleteFormModal";
```

(`updateForm` and `updateQuestions` are currently imported but unused — removing
them while you are on this line is optional tidy-up, not required.)

### F2. State

Extend the page state machine and add the modal toggle, mirroring
`showAddModal`, plus the lazily-fetched count:

```tsx
type PageState = 'loading' | 'ready' | 'saving' | 'deleting' | 'error';
```

```tsx
const [showDeleteModal, setShowDeleteModal] = useState(false);
const [submissionCount, setSubmissionCount] = useState<number | null>(null);
```

### F3. `openDeleteModal` — open now, count after

```tsx
function openDeleteModal() {
    if (!id)
        return;

    setSubmissionCount(null);
    setShowDeleteModal(true);

    getSubmissionCount(id)
        .then(count => setSubmissionCount(count))
        .catch(() => setSubmissionCount(null));
}
```

The dialog appears instantly and fills in the number when it arrives — no
spinner between the click and the warning.

**The `catch` is silent on purpose.** If the count request fails, the modal keeps
its `null` wording and the user can still delete. An error toast here would
report a failure of something the user never asked for, on top of a dialog
that is working fine.

`setSubmissionCount(null)` on every open resets a number left over from a
previous open, so a stale count is never shown against a fresh dialog.

### F4. `handleDelete`

Place it next to `handleSave`:

```tsx
async function handleDelete() {
    if (!id)
        return;

    setState('deleting');

    try {
        await deleteForm(id);

        showSuccess('Form deleted.');
        navigate('/dashboard', { replace: true });
    }
    catch (err: unknown) {
        showError(getErrorMessage(err, 'Failed to delete the form. Please try again.'));
        setState('ready');
        setShowDeleteModal(false);
    }
}
```

Three details that matter:

- **No `setState('ready')` on success.** The component unmounts on navigation;
  setting state on the way out would warn and the dialog would flicker back to
  its idle labels for a frame. The spinner stays until the route changes.
- **`{ replace: true }`.** The form is gone, so `/forms/:id/edit` would render
  the "Form not found" branch if the user pressed Back. Replacing the history
  entry removes that dead end. (The rest of the codebase uses plain
  `navigate('/dashboard')`; this call is the exception, on purpose.)
- **The success toast fires *before* navigating and still shows.** `<Toaster />`
  is mounted in `frontend/src/main.tsx` above `<App />` — outside the router — so
  it survives the route change.

### F5. The header button

In the existing action group (`FormEditorPage.tsx:167-174`), add Delete as the
first child and guard Save/Delete against each other:

```tsx
<div className="flex items-center gap-3">
    <Button
        variant="danger"
        onClick={openDeleteModal}
        disabled={state === 'saving' || state === 'deleting'}
    >
        Delete
    </Button>
    <Button variant="outline" onClick={() => navigate('/dashboard')}>
        Back
    </Button>
    <Button
        onClick={handleSave}
        isLoading={state === 'saving'}
        disabled={state === 'deleting'}
    >
        Save
    </Button>
</div>
```

### F6. Render the dialog

Next to the existing `{showAddModal && ...}` block at the end of the JSX:

```tsx
{showDeleteModal && (
    <DeleteFormModal
        formTitle={form.title}
        questionCount={questions.length}
        submissionCount={submissionCount}
        isDeleting={state === 'deleting'}
        onConfirm={handleDelete}
        onClose={() => setShowDeleteModal(false)}
    />
)}
```

`form.title` — the **saved** title from `FormDetail`, not the `title` state bound
to the header input. If the user typed a new title and hasn't saved, the dialog
must name the form as it exists on the server, which is what is being deleted.

---

## Part G — Verification

### G1. Build

```bash
dotnet build FormAI.sln
cd frontend && npm run build
```

### G2. Confirm the database really cascades (one-time check)

The EF configuration says `Cascade`, but confirm the live database agrees before
trusting it. `confdeltype = 'c'` means `ON DELETE CASCADE`:

```sql
select conrelid::regclass as child_table, conname, confdeltype
from pg_constraint
where contype = 'f'
  and conrelid::regclass::text in (
    'form_questions', 'question_options', 'submissions',
    'answers', 'answer_selected_options', 'form_source_contents')
order by 1;
```

Expect `c` on every row **except** `fk_submissions_users_user_id`, which is
`n` (`SET NULL`) by design — deleting a *user* nulls the submission's author; it
has nothing to do with deleting a form.

### G3. End-to-end delete with real data

1. Create a form with several questions (mix `Single`, `Multiple`, `Text`), mark
   it public, and save.
2. Answer it twice from incognito windows so `submissions`, `answers` and
   `answer_selected_options` all have rows.
3. Capture the form id and the before-counts:

```sql
\set fid '00000000-0000-0000-0000-000000000000'   -- the form id

select
  (select count(*) from form_questions where form_id = :'fid')                    as questions,
  (select count(*) from question_options o
     join form_questions q on q.id = o.question_id where q.form_id = :'fid')      as options,
  (select count(*) from submissions where form_id = :'fid')                       as submissions,
  (select count(*) from answers a
     join submissions s on s.id = a.submission_id where s.form_id = :'fid')       as answers,
  (select count(*) from answer_selected_options x
     join answers a on a.id = x.answer_id
     join submissions s on s.id = a.submission_id where s.form_id = :'fid')       as selected_options,
  (select count(*) from form_source_contents where form_id = :'fid')              as source_contents;
```

Every column must be non-zero (except `source_contents`, which is 0 for a
manually created form and non-zero for an AI-generated one — use a generated
form if you want to exercise that edge too).

4. Open the editor, press **Delete**. The dialog must appear immediately, name
   the form, and fill in the submission count a moment later — that number must
   match the `submissions` count above. Watch the network tab: exactly one
   `GET /api/forms/{id}/submissions/count`, fired on the click and **not** on
   page load.
5. Press **Delete form**. Expect: spinner on the confirm button → "Form deleted."
   toast → dashboard, with the form gone from the list.
6. Re-run the same query. **Every column must now be 0**, and
   `select * from forms where id = :'fid'` must return no rows.

### G4. The rest of the manual pass

| Case | Expected |
|---|---|
| Press Delete, then **Cancel** | Dialog closes, nothing deleted, editor untouched |
| Press Delete, then **Escape** | Same as Cancel |
| Press Delete, then click the **backdrop** | Same as Cancel |
| Click **inside** the dialog card | Dialog stays open |
| Escape / backdrop **while deleting** | Dialog stays open, request finishes |
| A form with **0 submissions** | Dialog reads "0 submissions and all answers"; deletes fine |
| A form with **1 question / 1 submission** | Singular wording: "1 question", "1 submission" |
| Delete **someone else's** form (call `DELETE /api/forms/{id}` with another user's token, e.g. via Swagger) | `403` with `{ "message": "You do not own this form" }`; through the UI it would surface as the error toast and the page returns to `ready` |
| Delete an **already-deleted** form (repeat the request) | `404 "Form not found"` — the endpoint is not idempotent; known and accepted |
| Press **Save** while the delete is in flight | Save button is disabled |
| Browser **Back** right after a delete | Lands on whatever preceded the editor, not the dead `/forms/:id/edit` |
| **Count endpoint for someone else's form** — `GET /api/forms/{id}/submissions/count` with another user's token | `403 "You do not own this form"` |
| **Count endpoint with no token** | `401` — the action inherits `FormsController`'s `[Authorize]` |
| **Count request fails** (block the URL in devtools, then press Delete) | Dialog still opens and reads "every submission received so far, and all answers"; no error toast; Delete still works |
| Open the dialog, Cancel, open it again | Count is refetched; no stale number flashes from the previous open |
| Anonymous `GET /api/forms/{public-id}` | Response is **unchanged** from before this phase — no `submissionCount` field, no extra query on the public read path |

---

## Files touched

| File | Change |
|---|---|
| `src/FormAI.Application/Interfaces/ISubmissionRepository.cs` | Add `CountByFormAsync` |
| `src/FormAI.Infrastructure/Repositories/SubmissionRepository.cs` | Implement `CountByFormAsync` |
| `src/FormAI.Application/Forms/GetSubmissionCount/GetSubmissionCountRequest.cs` | **New** — request + response records |
| `src/FormAI.Application/Forms/GetSubmissionCount/GetSubmissionCountHandler.cs` | **New** — owner check + count |
| `src/FormAI.API/Controllers/FormsController.cs` | **Only** adds `GET {id}/submissions/count` + its injected handler |
| `src/FormAI.Infrastructure/DependencyInjection.cs` | Register `GetSubmissionCountHandler` |
| `frontend/src/api/forms.ts` | Add `deleteForm(formId)` and `getSubmissionCount(formId)` |
| `frontend/src/components/Button.tsx` | Add `danger` variant; move focus ring into the variants |
| `frontend/src/components/ConfirmModal.tsx` | **New** — generic confirmation dialog |
| `frontend/src/components/editors/DeleteFormModal.tsx` | **New** — delete copy + counts over `ConfirmModal` |
| `frontend/src/pages/FormEditorPage.tsx` | Delete button, `'deleting'` state, `openDeleteModal`, `handleDelete`, dialog render |
| — | **No migration.** The `ON DELETE CASCADE` constraints already exist |
| — | **No change** to `GetFormResponse`, `GetFormHandler`, or `frontend/src/types/form.ts` — the count is its own endpoint |
| — | **No change** to `DeleteFormHandler`, `DeleteFormRequest`, `FormRepository.DeleteAsync`, or the existing `DELETE` action — already implemented |

---

## Follow-ups this phase deliberately does not do

- **Delete from the dashboard.** `FormCard` is a single full-card `<button>` with
  no actions slot; giving it a menu is its own piece of work. `ConfirmModal` will
  be waiting when it happens.
- **Integration test for the cascade.** `tests/FormAI.IntegrationTests` currently
  contains only `UserRepositoryTests.cs`, and `FormAI.UnitTests` has no files at
  all despite `CLAUDE.md` describing it. Verification here is the manual +
  `psql` pass in Part G.
- **Backend delete-path cleanup** — `DeleteFormHandler`'s `public readonly` field
  and discarded `bool`, the redundant `FindAsync` in `DeleteAsync`, missing
  `[ProducesResponseType]`, non-idempotent second delete.
- **Portal / focus trap for modals**, which `AddQuestionModal` also lacks.
