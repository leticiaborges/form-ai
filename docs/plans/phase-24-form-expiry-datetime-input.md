# Phase 24 — Owner-chosen expiry, and the date/time inputs behind it

## Context

Until now every generated form expired 15 days after creation, hardcoded in `GenerateFormHandler`. This
phase lets the owner pick the moment instead, on the Create form page, with a date box and a time box
side by side.

That needs the project's first `<input type="date">` and `<input type="time">`, so the phase also
introduces three reusable components:

- **`DateInput`** — a styled native date box.
- **`TimeInput`** — a styled native time box.
- **`DateTimeInput`** — the two of them together, exposing **one** value to its caller.

`DateInput` and `TimeInput` are near-identical, and they each restate the Tailwind string that
`Input.tsx` already has. **That duplication is accepted for this phase** — a deliberate choice to keep
three new files independent while the shape settles, not an oversight. Extracting a shared styled input
is a later, separate change.

### Why native inputs and not a picker library

`<input type="date">` and `<input type="time">` are supported by every browser this project targets, open
the OS picker on mobile, are keyboard- and screen-reader-correct for free, and cost nothing in bundle
size. The display format follows the user's locale while the **value** is always normalised —
`YYYY-MM-DD` for date, `HH:mm` for time — which is exactly the split we want.

What we give up: the popup calendar can't be restyled, and there are no date ranges or per-day disabling.
None of that is needed for a single expiry moment. If a future screen needs a range, reach for
`react-day-picker` then — and note it would still be paired with a native time input.

### The value format decision

`DateTimeInput`'s public value is a **local wall-clock string**, `"2026-09-20T14:30"` — no seconds, no
timezone offset. Empty string means "nothing picked yet".

This matters because it draws a line: the component and the form deal in *what the user sees on a wall
clock*, and exactly one place in the codebase converts that to UTC. If `DateTimeInput` exposed
`{ date, time }` instead, every caller would have to do the join itself and the timezone conversion would
end up copy-pasted.

### The UTC decision, and where the conversion happens

`Form.ExpiresAt` is written from `DateTime.UtcNow` elsewhere in the codebase, and `Form.IsExpired`
compares against `DateTime.UtcNow`. The column is therefore UTC, and the expiry the owner picks has to
arrive as UTC.

The conversion happens **once, in `frontend/src/api/forms.ts`**, and nowhere else:

```
"2026-09-20T14:30"   the user's wall clock, what DateTimeInput emits and zod validates
       │
       │  new Date(local)          ← parses an offset-less string as LOCAL time
       ▼
Date object
       │
       │  .toISOString()
       ▼
"2026-09-20T17:30:00.000Z"   what crosses the wire (Brazil is UTC-3)
       │
       │  System.Text.Json sees the trailing Z
       ▼
DateTime { Kind = Utc }   what the handler compares and the entity stores
```

The trailing `Z` is load-bearing. Without it, System.Text.Json produces `Kind = Unspecified`, and Npgsql
refuses to write a non-UTC `DateTime` to a `timestamp with time zone` column — a 500 at save time, not a
silent shift. Verification step 5 is there to prove this end to end.

### What is already implemented

You have already started this. Audit of the working tree as it stands:

| Piece | State |
|---|---|
| `DateInput.tsx`, `TimeInput.tsx` | Done — right input types, right styling, `forwardRef`, error border. Four small fixes in step 1. |
| `DateTimeInput.tsx` | **Renders both boxes but does nothing.** No split, no join, no `onChange`. Step 2 is the bulk of the phase. |
| `CreateFormPage` — `useController` | Correct hook, correctly wired. |
| `CreateFormPage` — zod schema | `expiresAt: z.date()` — **mismatched**, the component emits a string. Step 3. |
| `CreateFormPage` — `defaultValues` | Missing `expiresAt`, so the field starts uncontrolled. Step 3. |
| `forms.ts` | `expiresAt: Date` + `.toISOString()` — **already correct**, no change needed. |
| `GenerateFormRequest` | `DateTime ExpiresAt` added. Keep. |
| `GenerateFormHandler` | Uses `request.ExpiresAt` and rejects a past date — but throws `ArgumentException`, which the middleware maps to **500**. Step 5. |

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/DateInput.tsx` | four small fixes |
| `frontend/src/components/TimeInput.tsx` | same four, plus `step={60}` |
| `frontend/src/components/DateTimeInput.tsx` | **the real work** — own one value, split and join it |
| `frontend/src/pages/CreateFormPage.tsx` | schema, default value, markup, submit conversion |
| `frontend/src/api/forms.ts` | no change — already correct |
| `src/FormAI.Application/Forms/GenerateForm/GenerateFormHandler.cs` | `ValidationException` instead of `ArgumentException` |
| `docs/known-gaps.md`, `CLAUDE.md` | the expiry is now owner-chosen at creation |

---

## Step 1 — `DateInput.tsx` and `TimeInput.tsx`

Four changes, both files.

**1a. Drop the `inputId` alias.** `const inputId = id;` is a leftover from `Input.tsx`, where it existed
to derive an id from the label when none was passed. Here it renames a variable and nothing else. Use
`id` directly on the `<input>` and delete the line.

**1b. Default `showError` to `true`.** It is currently required, which makes standalone use noisy.
`DateTimeInput` will pass `false` explicitly:

```tsx
({ error, id, showError = true, className = '', ...props }, ref) => {
```

and in the interface: `showError?: boolean;`

**1c. Add `displayName`.** `forwardRef()` returns an object, not a function, so it has no `.name` for
React to read — DevTools and every component stack will show `ForwardRef` instead of the real name. Since
the render function passed in is an anonymous arrow, there isn't even a name to borrow. Add below each
component, matching `Input.tsx:34`:

```tsx
DateInput.displayName = 'DateInput';
```

You will hit React's controlled-input warnings while building step 2 — this is what makes those warnings
name the right component.

> Worth knowing: on React 19 (this project is on 19.2) `ref` is an ordinary prop and `forwardRef` is a
> compatibility shim. `export function DateInput({ id, ref, ...props })` would be the modern form and the
> `displayName` question would disappear. Not doing that here, because `Input.tsx` still uses
> `forwardRef` and a half-converted folder is worse than a consistently old-style one. Converting all
> three plus `Input.tsx` is a clean separate change.

**1d. `TimeInput` only — default `step={60}`:**

```tsx
<input id={id} type="time" step={60} ref={ref} ... {...props} />
```

Put `step` **before** `{...props}` so a caller can still override it. `60` seconds is what gives you an
`HH:mm` box; any value that is not a multiple of 60 makes Chrome grow a seconds field. Owning this
default is one of the reasons the component exists.

---

## Step 2 — `DateTimeInput.tsx`

Replace the file. Read the notes underneath before typing it in — this is the part worth understanding.

```tsx
import { useState } from "react";
import { DateInput } from "./DateInput";
import { TimeInput } from "./TimeInput";

interface DateTimeInputProps {
  id: string;
  label: string;
  /** Local wall-clock, "YYYY-MM-DDTHH:mm". Empty string means nothing picked yet. */
  value: string;
  onChange: (value: string) => void;
  onBlur?: () => void;
  error?: string;
  disabled?: boolean;
  /** Earliest date the picker offers, "YYYY-MM-DD". A convenience, never validation. */
  minDate?: string;
}

/** "2026-09-20T14:30" -> { date: "2026-09-20", time: "14:30" }. Tolerates a half-filled value. */
function splitValue(value: string) {
  const [date = '', time = ''] = value.split('T');
  return { date, time };
}

export function DateTimeInput({
  id, label, value, onChange, onBlur, error, disabled, minDate
}: Readonly<DateTimeInputProps>) {

  // The two halves live here because a half-filled pair has no valid joined form: the user picks a
  // date before a time, and if we emitted '' for that state the date they just picked would be gone
  // on the next render.
  const [parts, setParts] = useState(() => splitValue(value));

  function update(next: { date: string; time: string }) {
    setParts(next);
    onChange(next.date && next.time ? `${next.date}T${next.time}` : '');
  }

  return (
    <fieldset disabled={disabled} className="flex flex-col gap-1">
      <legend className="text-sm font-medium text-gray-700">{label}</legend>

      <div className="flex items-center gap-2">
        <div className="w-44">
          <DateInput
            id={`${id}-date`}
            aria-label="Date"
            value={parts.date}
            min={minDate}
            error={error}
            showError={false}
            onChange={e => update({ date: e.target.value, time: parts.time })}
            onBlur={onBlur}
          />
        </div>
        <div className="w-32">
          <TimeInput
            id={`${id}-time`}
            aria-label="Time"
            value={parts.time}
            error={error}
            showError={false}
            onChange={e => update({ date: parts.date, time: e.target.value })}
            onBlur={onBlur}
          />
        </div>
      </div>

      {error && <span className="text-xs text-red-500">{error}</span>}
    </fieldset>
  );
}
```

Points worth understanding rather than copying:

- **The props type is purpose-built, not `InputHTMLAttributes<HTMLInputElement>`.** That base is what made
  the current version pass one `value` to both boxes: `<input type="date">` given `"2026-09-20T14:30"`
  doesn't parse it and renders blank. This component is not one input, and its type should say so.
  A bonus: nobody can now pass it `type="email"`.

- **`value` and `onChange` travel together.** A `value` with no `onChange` makes a React input read-only
  and logs a warning — that is the state the file is in right now.

- **One error, rendered once.** "The expiry must be in the future" is about the pair, not about either
  box. So the children get `error` (for the red border) with `showError={false}` (no message), and the
  message is rendered here. That is exactly what the `showError` prop was added for.

- **`<fieldset>` + `<legend>`, not `<label>`.** A `<label htmlFor>` can only point at one control, and
  there are two. `fieldset`/`legend` is the standard grouping, and `aria-label` on each child names it
  within the group. As a bonus, `disabled` on a `fieldset` natively disables everything inside it — no
  prop drilling.

- **The widths are on wrapper `<div>`s, not passed as `className`.** The children hardcode `w-full`, and
  passing `w-44` through `className` would put two width utilities on the same element; Tailwind resolves
  that by stylesheet order, not by the order you wrote them, so it is unpredictable. Sizing from the
  outside and letting the input fill its box always works.

- **`minDate` is a convenience, never validation.** It greys out earlier days in the picker. A typed value
  can still get past it, and the user can edit the DOM. The real checks are in zod (step 3) and in the
  handler (step 5).

- **Known limitation, accepted:** because the halves live in `useState` seeded once, an external change to
  `value` after mount — `form.reset()`, or loading an existing form — will not reach the boxes. Nothing on
  the Create form page does that. When a screen needs it (editing an existing form's expiry), the fix is
  a `useEffect` that re-splits `value` when it differs from the joined state. Don't add it speculatively.

---

## Step 3 — `CreateFormPage.tsx`

**3a. Remove the unused `Input` import.** It was added but never used; `noUnusedLocals` and Sonar will
both flag it.

**3b. Fix the schema.** `z.date()` cannot match what `DateTimeInput` emits — the field value is a string.
Validate it as a string, and convert at the boundary:

```ts
expiresAt: z.string()
  .min(1, "Pick an expiry date and time.")
  .refine(v => !Number.isNaN(Date.parse(v)), "Enter a valid date and time.")
  .refine(v => new Date(v) > new Date(), "The expiry must be in the future."),
```

Three separate rules on purpose, so the message tells the user which one they broke. The order matters —
`.refine` runs in sequence, so the parse check guards the comparison that follows it.

`new Date("2026-09-20T14:30")` — an ISO string with **no** offset — is parsed as *local* time by
specification. That is what makes the future-comparison mean what the user expects, and it is the same
call `forms.ts` relies on later.

**3c. Give it a default value:**

```ts
defaultValues: {
  questionCount: 5,
  difficultyLevel: "Medium",
  isGraded: false,
  expiresAt: ''
}
```

Without this, `field.value` starts `undefined`, the inputs mount uncontrolled, and React logs the
"changing an uncontrolled input to be controlled" warning on the first pick.

**3d. Replace the markup.** The current block wraps `DateTimeInput` in its own `<label>` — drop it, the
component owns its `legend` now:

```tsx
<DateTimeInput
  id="expiresAt"
  label="Expires at"
  value={field.value ?? ''}
  onChange={field.onChange}
  onBlur={field.onBlur}
  error={fieldState.error?.message}
/>
```

Note the id is `expiresAt`, matching the field name. The old `htmlFor="expiryDate"` could never have
matched anything — the children carry suffixed ids.

**3e. Convert to a `Date` on submit.** `CreateFormData.expiresAt` is a string; `GenerateFormPayload`
expects a `Date`:

```ts
const result = await generateForm({ ...data, expiresAt: new Date(data.expiresAt) });
```

This is the one line where wall-clock becomes an absolute instant. Everything downstream —
`.toISOString()` in `forms.ts`, the `Z`, `Kind = Utc` on the server — follows from it.

---

## Step 4 — `frontend/src/api/forms.ts`

**No change.** `expiresAt: Date` on the payload and `payload.expiresAt.toISOString()` in the body are
already right. Listed here only so you don't go looking.

Read the two lines once with the diagram from the Context section in mind — this file is the single
place where local time becomes UTC, and it is worth knowing that by heart rather than rediscovering it.

---

## Step 5 — `GenerateFormHandler.cs`

The expiry check exists but throws the wrong exception type.

`ExceptionHandlingMiddleware` maps `NotFoundException`, `ForbiddenException`, `ValidationException` and
`UnauthorizedAccessException`. `ArgumentException` falls into the `_ =>` arm: **500**, the body
`"An unexpected error occurred."`, and a logged "Unhandled exception" for what is ordinary user input.

Replace lines 53–54:

```csharp
if (request.ExpiresAt <= DateTime.UtcNow)
    throw new ValidationException(new Dictionary<string, string[]>
    {
        ["expiresAt"] = ["The expiry must be in the future."]
    });
```

and add the using:

```csharp
using FormAI.Application.Common.Exceptions;
```

The key is `expiresAt`, camelCase, matching the client field name and the convention in
`RegisterHandler.cs:32` (`["email"]`) and `SubmitFormHandler.cs:77` (`["form"]`).

Things to know, not to change:

- **Why the check is here at all**, when zod already ran: the client check is UX, the server check is the
  rule. A request that skips the browser still has to be rejected.

- **`request.ExpiresAt` is `Kind = Utc`** because the JSON string ends in `Z`, so comparing it to
  `DateTime.UtcNow` compares like with like. Drop the `Z` and you would be comparing an
  `Unspecified` local time against UTC and silently accepting expiries three hours in the past.

- **`ExpiresAt` is non-nullable on the request while `Form.ExpiresAt` is nullable.** `CONTEXT.md` says a
  form with no expiry accepts submissions indefinitely, but this endpoint can no longer produce one — an
  omitted `expiresAt` binds to `default(DateTime)` (year 0001) and fails the future check. That is
  acceptable while the UI always asks, and step 6 records it.

- **The `ArgumentException`s above it** (title too long, source text empty) have the same 500 problem. Out
  of scope for this phase; fixing them is a tidy separate commit.

- The toast on the Create page uses `getErrorMessage`, which reads only `message` — so the user will see
  the generic *"One or more validation errors occurred."* rather than the per-field text. The zod rule in
  step 3b is what actually gives them a useful message. Surfacing the server's `errors` dictionary is a
  wider change than this phase.

---

## Step 6 — Docs

**6a. `docs/known-gaps.md`** — the **"Editing the expiry date"** row (line 17) is now half false. Replace
it:

```markdown
| **Editing the expiry date** | The owner picks an expiry when generating a form, but nothing sends `ExpiresAt` after creation — `SaveFormEditorHandler` preserves whatever is stored, and the endpoint that could change it (`PUT /api/forms/{id}`) has been removed. `POST /forms/generate/text` also requires an expiry, so a form that never expires cannot be created from the UI even though the domain allows one. |
```

**6b. `CLAUDE.md`, "Business rules"** — under *Forms and questions*, replace:

> Generated forms are created **private**, with `ShowResultsAfterSubmit = false` and an expiry **15 days out**.

with:

```markdown
- Generated forms are created **private**, with `ShowResultsAfterSubmit = false` and the expiry the owner
  picked on the Create form page. The expiry is required and must be in the future; it arrives as UTC and
  is compared against `DateTime.UtcNow`.
```

**6c. `CLAUDE.md`, "What works end to end today"** — change *"paste text"* to *"paste text and pick when
the form expires"*.

**6d. `CLAUDE.md`, "Known gaps" headline** — the list names *"editable expiry"* among things not built.
Narrow it to *"editing the expiry after creation"*.

**6e. `CONTEXT.md`** — no change needed. **Expiry** and **Expired form** are already defined, and this
phase introduces no new term.

---

## Verification

### 1. Build

```bash
cd frontend && npm run build
dotnet build FormAI.sln
```

The likeliest break is step 3b — if the schema still says `z.date()`, TypeScript will complain that
`field.value` is not assignable to `string` in step 3d.

### 2. The console must be clean

Open the Create form page with DevTools open. **No** "changing an uncontrolled input to be controlled"
and **no** "you provided a `value` prop without an `onChange` handler". Either one means step 3c or step 2
is incomplete.

### 3. The two boxes behave as one field

- Pick a date only → the time box stays empty, the date **stays visible**. (If it clears, the `useState`
  in step 2 is missing.)
- Pick a time too → both hold their values.
- Submit with only a date → *"Pick an expiry date and time."* under **both** boxes, once, with both
  borders red.
- The time box shows `HH:mm` with **no seconds segment** — that is step 1d.
- Tab through: date, then time, then the next field. Both should announce as "Date" / "Time" within an
  "Expires at" group.

### 4. Past dates

Pick yesterday and submit. Blocked client-side by the zod refine, with *"The expiry must be in the
future."* No network request should be made — check the Network tab.

### 5. The UTC round trip — the one that matters

Pick a time you can recognise, say **20:00 today**, and generate.

- In the Network tab, the request body reads `"expiresAt":"2026-09-09T23:00:00.000Z"` — three hours later
  than what you typed, if you are on UTC-3. **That shift is correct.** No `Z`, or the same digits you
  typed, means step 3e or `forms.ts` is wrong.
- Then check the row:

```bash
docker compose exec postgres psql -U postgres -d formai \
  -c "select title, expires_at from forms order by created_at desc limit 1;"
```

The stored value should be `23:00`, not `20:00`. A **500** here instead of a row is the Npgsql
`Kind = Unspecified` failure described in the Context section — the `Z` went missing somewhere.

### 6. The server rejects a past date properly

With the API running, bypass the browser:

```bash
curl -i -X POST http://localhost:5155/api/forms/generate/text \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"sourceText":"...", "questionCount":3, "difficultyLevel":"Medium", "isGraded":false,
       "sourceType":"Text", "expiresAt":"2020-01-01T00:00:00.000Z"}'
```

Expect **400** with `"One or more validation errors occurred."` and an `errors` object keyed `expiresAt`.
A **500** means step 5 was not applied.

### 7. The expiry actually takes effect

Generate a form expiring two minutes out, publish it, open the share link in a private window and submit
— it should work. Wait for the expiry to pass, then try again: *"This form is no longer accepting
submissions."* from `SubmitFormHandler.cs:77`. The owner can still open and read it.

### 8. Full regression

```bash
dotnet test FormAI.sln
```

No test covers this path yet — the run is to confirm the `GenerateFormRequest` signature change broke
nothing that constructs it.

---

## After this phase

- **Editing the expiry after creation** is still not possible, and is the obvious next phase: the editor
  would need `ExpiresAt` on `SaveFormEditorRequest`, and `DateTimeInput` would need the `useEffect` sync
  noted in step 2 to show a stored value.
- **"Never expires" is unreachable from the UI**, though the domain supports it. A nullable request field
  plus a "no expiry" checkbox would close that.
- **The duplication between `DateInput` and `TimeInput`** — and between both of them and `Input.tsx` — is
  now three copies of the same Tailwind string. Extracting a shared styled input, and deciding at the same
  time whether to drop `forwardRef` for React 19's ref-as-prop across all four, is a small self-contained
  change.
- **Displaying an expiry** anywhere (dashboard card, editor header) will need the reverse conversion: the
  API returns UTC, and rendering it raw would show a time three hours off. Nothing displays it today,
  which is why this phase doesn't solve it.
