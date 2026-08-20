# Phase 12: Shared toast notification pattern (success/error/warning)

## Context

`CreateFormPage.tsx`, `DashboardPage.tsx`, `FormEditorPage.tsx`, and
`FormAnswerPage.tsx` each grew their own ad-hoc way of showing feedback: a
`serverError`/`saveError`/`submitError` `useState` rendered as a red `<p>` or
banner `<div>` somewhere in the JSX. A few problems fell out of that:

- **No success feedback anywhere.** Saving a form in `FormEditorPage`
  (`handleSave`) silently returns to the `'ready'` state with nothing telling
  the user it worked — the concrete bug that started this phase.
- **Duplicated, drifting error-extraction code.** Most pages do
  `const e = err as CustomResponse; e.response?.data?.message ?? '<fallback>'`
  inline in their `catch` block, but `FormEditorPage`'s `handleSave` doesn't —
  it just hardcodes `'Failed to save. Please try again'` and throws away the
  real server message.
- **A hand-rolled "toast" that isn't reusable.** `FormEditorPage`'s copy-link
  button already does the transient-success-message thing (a `copied` boolean
  state + `setTimeout(() => setCopied(false), 1500)` that shows a green
  checkmark for 1.5s) — it's proof the app already *wants* this pattern, it
  just built one throwaway instance of it instead of a shared one.

This phase introduces a single toast library (**sonner**) and two small
shared utility modules, then migrates all four pages onto them. Login/
Register/VerifyEmail pages and all **field-level** validation errors (the red
text under a specific input) are explicitly **out of scope** — those stay
exactly as they are today. The reasoning: a toast is for a *page-level* event
("the save succeeded/failed"), not for "this specific input is invalid" —
field errors need to stay pinned next to the field the user is looking at,
not fly by in a corner of the screen.

---

## Why sonner, and why a wrapper around it

The repo has zero UI/toast libraries today (`frontend/package.json` — checked,
nothing like `react-hot-toast`/`react-toastify`/`sonner`/`notistack`, no MUI/
Chakra/Radix either; every component is hand-rolled Tailwind). `sonner` is a
small, actively maintained toast library built for React that ships its own
`success`/`error`/`warning` styling out of the box (`richColors`), so we don't
have to invent a new red/green/amber design system just for this.

Rather than calling `toast.success(...)` from `sonner` directly in every page,
we add one tiny wrapper module (`frontend/src/utils/toast.ts`). This isn't
strictly required — it's a "seam" so that if the team ever swaps `sonner` for
something else, or wants to add analytics/logging on every toast, there's one
place to change instead of N call sites. It costs three trivial functions, so
the payback threshold is very low.

---

## Part A — Install the dependency

```bash
cd frontend
npm install sonner
```

This adds `sonner` to `frontend/package.json` `dependencies`. No other config
file changes needed — sonner ships its own CSS-in-JS styling engine, so
nothing needs to be added to `frontend/src/index.css` (which only defines the
Tailwind v4 `@theme` tokens like the `brand` color scale).

---

## Part B — Mount the `<Toaster />` once, globally

### B1. `frontend/src/main.tsx`

Read the current file first — it should look like:

```tsx
createRoot(rootElement).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
```

Add the import and render `<Toaster />` as a sibling to `<App />`:

```tsx
import { Toaster } from 'sonner';

createRoot(rootElement).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
        <Toaster position="top-right" richColors closeButton duration={4000} theme="light" />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
);
```

**Why here, and why not inside `BasePage.tsx`:** `BasePage` is the layout
shell used by the "logged-in app" pages (Dashboard, CreateForm, FormEditor,
FormAnswer) — it renders a `<header>` + content area. But `LoginPage`,
`RegisterPage`, `VerifyEmailPage`, `RegisterSuccessPage`, and `LandingPage`
**don't** use `BasePage` at all; they render their own centered full-page
wrapper. If we put `<Toaster />` inside `BasePage`, it would only exist on
half the app's pages. Mounting it once at the very root (next to `<App />`,
which is where all the `<Routes>` live) means every page — whether or not it
uses `BasePage` — can call `showSuccess`/`showError` and have it actually
render. (This migration only wires up calls from the four in-scope pages, but
the mount point being global means the auth pages could adopt this pattern
later with zero setup changes.)

**Why `<Toaster />` survives navigation:** it's a sibling of `<App />`, not a
child of any particular route's element. React Router swaps out what's
*inside* `<App />`'s `<Routes>` on navigation, but `<Toaster />` itself never
unmounts. That matters concretely in `CreateFormPage`: we fire a success toast
right before `navigate(...)` to the form editor — because `<Toaster />` is
mounted above the router outlet, the toast stays visible through that
navigation instead of vanishing with the page it was triggered from.

**Config choices explained:**
- `richColors` — turns on sonner's built-in green (success) / red (error) /
  amber (warning) styling. We're deliberately *not* trying to hand-theme
  toasts to pixel-match the old `bg-red-50 border-red-200` banners — those
  banners are being deleted, not ported forward.
- `closeButton` — adds a small "×" so a user can dismiss a message early
  instead of waiting out the timer.
- `duration={4000}` — how long a toast stays up (ms) before auto-dismissing.
  4 seconds is sonner's own default; being explicit here just makes the
  choice visible/discoverable in code instead of implicit.
- `theme="light"` — the rest of this app's UI has no dark mode (all
  components hardcode `bg-white`/`bg-gray-50` etc.), so forcing light avoids a
  jarring dark toast popping up on an otherwise all-light page.
- `position="top-right"` — a conventional, unobtrusive corner that doesn't
  overlap `BasePage`'s `<header>` (which flows normally, not fixed/sticky, so
  there's no literal overlap risk either way) or the sticky header in
  `FormEditorPage`.

---

## Part C — The two shared utility modules

### C1. `frontend/src/utils/toast.ts` (new file)

```ts
import { toast } from 'sonner';

export function showSuccess(message: string) {
  toast.success(message);
}

export function showError(message: string) {
  toast.error(message);
}

export function showWarning(message: string) {
  toast.warning(message);
}
```

Three named functions rather than one `notify(type, message)` — this matches
how the rest of the codebase is written (small, single-purpose functions like
`getForm`, `submitForm` in `api/forms.ts`/`api/submissions.ts`), and reads
better at the call site: `showError(...)` tells you what happens without
having to also read a `'error'` string argument.

`showWarning` has no caller yet in this migration (none of the four pages'
current failure modes are "warnings" — they're all clear-cut success/error).
It's included anyway for API completeness, since the original ask was
specifically for a 3-state (success/error/warning) pattern that future pages
can reach for.

### C2. `frontend/src/utils/getErrorMessage.ts` (new file)

```ts
import type { CustomResponse } from '../types/CustomResponse';

export function getErrorMessage(err: unknown, fallback: string): string {
  const e = err as CustomResponse;
  return e.response?.data?.message ?? fallback;
}
```

This is a straight extraction of the idiom that's already duplicated in
`CreateFormPage.tsx` and `FormAnswerPage.tsx`:

```ts
const e = err as CustomResponse;
setServerError(e.response?.data?.message ?? "Failed to generate form. Please try again.");
```

becomes, everywhere it's used:

```ts
showError(getErrorMessage(err, 'Failed to generate form. Please try again.'));
```

`CustomResponse` (in `frontend/src/types/CustomResponse.ts`) is **not**
changed — it already models the axios error shape
(`{ response?: { data?: { message?: string; errors?: Record<string,string[]> } } }`).
Note `getErrorMessage` intentionally only reads `.message`, not `.errors` —
the `errors` field (per-field validation map) is what `RegisterPage` feeds
into `react-hook-form`'s `setError` for field-level errors, which stays
completely untouched by this phase. A toast is for the one-line summary; the
field-by-field breakdown (when the API returns one) still belongs inline.

---

## Part D — Per-page migrations

For each page below: read the current file, then apply the described
removals/additions. Line numbers are from the versions read while writing
this plan — they may have drifted slightly, use them as a starting point, not
gospel.

### D1. `frontend/src/pages/CreateFormPage.tsx`

**Remove:**
- `const [serverError, setServerError] = useState<string | null>(null);`
  (line 25).
- The banner block:
  ```tsx
  {serverError && (
    <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700">
      {serverError}
    </div>
  )}
  ```
  (lines 172-176).
- `import type { CustomResponse } from "../types/CustomResponse";` (line 9) —
  no longer needed once the inline cast is gone; double-check nothing else in
  the file references `CustomResponse` before deleting the import (it
  doesn't).

**Add imports:**
```ts
import { showSuccess, showError } from "../utils/toast";
import { getErrorMessage } from "../utils/getErrorMessage";
```

**Change `onSubmit`** (currently lines 40-49):

```ts
async function onSubmit(data: CreateFormData) {
  setServerError(null);
  try {
    const result = await generateForm(data);
    navigate(`/forms/${result.formId}/edit`);
  } catch (err: unknown) {
    const e = err as CustomResponse;
    setServerError(e.response?.data?.message ?? "Failed to generate form. Please try again.");
  }
}
```

becomes:

```ts
async function onSubmit(data: CreateFormData) {
  try {
    const result = await generateForm(data);
    showSuccess('Form generated successfully.');
    navigate(`/forms/${result.formId}/edit`);
  } catch (err: unknown) {
    showError(getErrorMessage(err, 'Failed to generate form. Please try again.'));
  }
}
```

Note this is the first page in the app to show a **success** toast on this
kind of action — there was no equivalent message before (it just silently
navigated away). Fire `showSuccess` *before* `navigate(...)` — the order
doesn't affect whether the toast shows (per Part B, `<Toaster />` survives
navigation either way), but it reads naturally as "it worked, now go there."

**Leave untouched:** all the `errors.title`/`errors.description`/etc.
react-hook-form/zod field errors — those are a different, in-scope-excluded
concern (field-level, not page-level).

### D2. `frontend/src/pages/DashboardPage.tsx`

This page is the one exception to "replace the inline text with a toast" —
it **keeps both**. Reasoning: a toast auto-dismisses after 4 seconds
(`duration={4000}` from Part B). If a user is scrolled away, mid-conversation,
or just slow to look up, they can easily miss a toast that already vanished —
and then be left staring at an empty forms list with no idea why. The inline
`state === 'error'` text is a *persistent* explanation that doesn't depend on
timing, so it's worth keeping alongside the toast, not instead of it.

**Change** the `useEffect` (currently lines 21-26):

```ts
useEffect(() => {
  listForms().then(data => {
    setForms(data);
    setState('ready');
  }).catch(() => setState('error'));
}, []);
```

becomes:

```ts
useEffect(() => {
  listForms().then(data => {
    setForms(data);
    setState('ready');
  }).catch(err => {
    setState('error');
    showError(getErrorMessage(err, "Couldn't load your forms. Please try again later."));
  });
}, []);
```

**Add import:**
```ts
import { showError } from "../utils/toast";
import { getErrorMessage } from "../utils/getErrorMessage";
```

**Leave untouched:** the `{state === 'error' && <p className="text-red-600">...</p>}`
block (line ~46-48) — keep the inline text exactly as it is today.

*(Side note while implementing: confirm `listForms()` in
`frontend/src/api/forms.ts` actually rejects with the raw axios error object
— i.e. that `err` reaching this `catch` really has the `CustomResponse`
shape. If `listForms` swallows/transforms the error itself, `getErrorMessage`
will just fall through to the fallback string, which is still correct
behavior, just worth knowing.)*

### D3. `frontend/src/pages/FormEditorPage.tsx`

This page has three separate spots to change — it's the one this whole phase
was triggered by (no save-success message).

**a) Save error — fix a real bug while migrating it.**

Currently `handleSave`'s `catch` (lines 117-120) is:

```ts
catch {
  setSaveError('Failed to save. Please try again');
  setState('ready');
}
```

Notice this is a bare `catch { }` — it never looks at the actual error, so
even if the backend returns a specific validation message, the user always
sees the same generic string. Change it to:

```ts
catch (err: unknown) {
  showError(getErrorMessage(err, 'Failed to save. Please try again.'));
  setState('ready');
}
```

This both migrates it to a toast *and* fixes the bug — the real server
message (when there is one) now reaches the user, matching how
`CreateFormPage`/`FormAnswerPage` already behave.

**b) Save success — the actual fix for the reported gap.**

In the `try` block of `handleSave` (lines 103-116), after the form state is
updated and right before `setState('ready')`, add a success toast:

```ts
try {
  await saveFormEditor(id, {
    title: trimmedTitle,
    description: trimmedDescription,
    isPublic,
    questions
  });
  setForm(f => f ? {
    ...f, title: trimmedTitle,
    description: trimmedDescription,
    isPublic
  } : f);
  showSuccess('Form saved.');
  setState('ready');
}
```

**Remove:**
- `const [saveError, setSaveError] = useState('');` (line 23).
- `setSaveError('');` at the top of `handleSave` (line 100) — no longer
  needed since there's no more `saveError` state to clear before a new
  attempt.
- The banner:
  ```tsx
  {saveError && (
    <p className="text-center text-sm text-red-600">{saveError}</p>
  )}
  ```
  (lines 267-269).

**c) Copy-link "Copied!" — replace the hand-rolled transient state with a real toast.**

Currently (lines 70-78):

```ts
async function handleCopyLink(url: string) {
  try {
    await navigator.clipboard.writeText(url);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  } catch {
    // clipboard API unavailable/denied — silently ignore
  }
}
```

becomes:

```ts
async function handleCopyLink(url: string) {
  try {
    await navigator.clipboard.writeText(url);
    showSuccess('Link copied to clipboard.');
  } catch {
    showError('Could not copy link. Please copy it manually.');
  }
}
```

Note the `catch` branch changes behavior slightly: today a clipboard failure
(permission denied, API unavailable in an insecure context, etc.) is silently
swallowed — the user clicks "copy" and nothing visibly happens, with no
explanation. Surfacing it as an error toast is a small UX improvement in the
same spirit as this whole phase (make outcomes visible), even though it
wasn't explicitly requested — flagging it here so it's a deliberate choice,
not a surprise.

**Remove:**
- `const [copied, setCopied] = useState(false);` (line 32).
- The conditional icon/text in JSX (lines 224-236):
  ```tsx
  {copied ? (
    <>
      <svg className="h-3.5 w-3.5 text-green-600" ...>...</svg>
      <span className="text-green-600">Copied!</span>
    </>
  ) : (
    <svg className="h-3.5 w-3.5" ...>...</svg>
  )}
  ```
  Replace with just the plain (previously "else branch") copy icon,
  unconditionally:
  ```tsx
  <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
  </svg>
  ```

**Add imports:**
```ts
import { showSuccess, showError } from "../utils/toast";
import { getErrorMessage } from "../utils/getErrorMessage";
```

**Leave untouched:** `titleError`/`descriptionError` state and their inline
`<p className="text-xs text-red-500 ...">` renderings — those are field-level
validation (title too long, description too long), out of scope by design.

### D4. `frontend/src/pages/FormAnswerPage.tsx`

This is the page the original request named, even though — as discussed
during planning — its terminal-state screens (already-submitted / expired /
submitted-success / load-error, rendered via `getMessageBasedOnState` and the
early-return block around lines 156-169) are intentionally staying as
**full-page** messages, not toasts. Those replace the entire page because the
respondent's interaction with the form is over at that point (they can't go
back and change anything) — a page-level confirmation screen fits that better
than a 4-second toast that could be missed entirely. The one thing that *does*
become a toast here is the submit-failure case, where the user is still on
the form and needs a transient nudge to try again.

**Remove:**
- `const [submitError, setSubmitError] = useState('');` (line 38).
- `setSubmitError('');` in `handleSubmit` (line 122).
- `{submitError && <p className="text-center text-sm text-red-600">{submitError}</p>}`
  (line 193).
- `import type { CustomResponse } from "../types/CustomResponse";` (line 6) —
  confirm nothing else in the file uses it before removing (it doesn't;
  `fieldErrors` is unrelated, plain `Record<string,string>` state).

**Add imports:**
```ts
import { showError } from "../utils/toast";
import { getErrorMessage } from "../utils/getErrorMessage";
```

**Change** the `catch` block in `handleSubmit` (currently lines 137-143):

```ts
catch (err: unknown) {
  const e = err as CustomResponse;
  setSubmitError(e.response?.data?.message
      ?? 'Failed to submit. Please try again.'
  );
  setState('ready');
}
```

becomes:

```ts
catch (err: unknown) {
  showError(getErrorMessage(err, 'Failed to submit. Please try again.'));
  setState('ready');
}
```

**Leave completely untouched:**
- `getMessageBasedOnState` and the whole early-return branch that renders
  full-page terminal states.
- `fieldErrors` / the "This question is required." inline validation
  (rendered via `QuestionAnswerCard`'s `error` prop) — this is a client-side
  check that runs *before* the `try`/`catch`, so it was never part of the
  toast conversation to begin with.

---

## Out of scope (confirmed, do not touch)

- `LoginPage.tsx`, `RegisterPage.tsx`, `VerifyEmailPage.tsx`,
  `RegisterSuccessPage.tsx`, `LandingPage.tsx` — keep their existing inline
  red-banner (`serverError` + `bg-red-50 border-red-200`) and `StatusCard`
  patterns exactly as they are.
- Every field-level validation error across all pages (react-hook-form/zod
  errors, `titleError`/`descriptionError` in `FormEditorPage`, `fieldErrors`
  in `FormAnswerPage`).
- `frontend/src/components/BasePage.tsx`, `Button.tsx` — no changes needed;
  toast rendering is independent of the layout shell (see Part B).
- `frontend/src/types/CustomResponse.ts` — reused as-is.
- `frontend/src/context/AuthProvider.tsx`/`AuthContext.tsx`/`useAuth.ts` — no
  changes; the toast wrapper is a stateless utility, not a context provider,
  so no provider nesting is needed beyond the single `<Toaster />` in
  `main.tsx`.

---

## Verification (manual — this repo has no automated frontend UI tests)

1. `cd frontend && npm install sonner && npm run dev` — app should boot with
   no console errors. The toast container is invisible until something
   triggers a toast, so no visual change yet on any page.
2. **CreateFormPage** (`/forms/new`):
   - Submit valid data with the backend running → green "Form generated
     successfully." toast appears and stays visible through the navigation to
     `/forms/:id/edit`.
   - Stop the backend (or otherwise force a request failure) and submit →
     red error toast with the fallback or server message; the zod field
     errors (e.g. leaving "Source content" too short) still work
     independently and don't involve a toast at all.
3. **DashboardPage** (`/dashboard`):
   - Stop the backend, reload `/dashboard` → both the inline "Couldn't load
     your forms..." text *and* a red error toast appear together.
4. **FormEditorPage** (`/forms/:id/edit`):
   - Edit a title/question, click Save → green "Form saved." toast, no more
     inline red text anywhere on a failure-free save.
   - Force a save failure (stop the backend mid-edit, click Save) → red toast
     with the real server message (verify it's no longer always the
     hardcoded "Failed to save. Please try again" string when the backend
     actually returns something more specific).
   - Toggle the form to Public, click the copy-link icon → clipboard receives
     the URL, green "Link copied to clipboard." toast appears, and the old
     inline checkmark+"Copied!" text no longer renders anywhere.
5. **FormAnswerPage** (`/forms/:id/answer`):
   - Leave a required question blank and submit → inline "This question is
     required." appears under that question only; no toast fires (this path
     never reaches the `try`/`catch`).
   - Force a submit failure (e.g. submit the same form twice quickly to
     trigger the backend's duplicate-submission rejection, or stop the
     backend) → red error toast, and the page stays on the answer form
     (does not switch to the full-page state).
   - Submit successfully → still shows the full-page "Thanks! Your response
     has been recorded." screen exactly as before, no toast involved.
6. **Cross-cutting:**
   - Trigger two toasts in quick succession on the same page (e.g. two failed
     submits) and confirm they stack instead of one replacing the other —
     this is sonner's default behavior, no extra code needed for it.
   - Visit `/login` and `/register` during this pass and confirm no toast
     ever appears there (nothing in this phase calls `showSuccess`/`showError`
     from those pages).

### Files touched

- `frontend/package.json` (new dependency: `sonner`)
- `frontend/src/main.tsx` (mount `<Toaster />`)
- `frontend/src/utils/toast.ts` (new)
- `frontend/src/utils/getErrorMessage.ts` (new)
- `frontend/src/pages/CreateFormPage.tsx`
- `frontend/src/pages/DashboardPage.tsx`
- `frontend/src/pages/FormEditorPage.tsx`
- `frontend/src/pages/FormAnswerPage.tsx`
