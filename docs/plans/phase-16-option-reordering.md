# Phase 16 — Drag-and-drop reordering of question options

## Context

Questions in the form editor can already be reordered by dragging a rail on the right edge of each
`QuestionCard` (dnd-kit, introduced in `phase-4-drag-and-drop-ordering.md`). Their **options** cannot.
Today the only way to change option order is to delete an option and re-add it, which mints a new
option id — and since phases 13/14 the option id is what ties a submitted answer to its option row in
`answer_selected_options`. So reordering by delete/re-add silently damages answer history.

This phase gives options the same drag-to-reorder interaction, in both places options are edited:

- the `QuestionCard` on the form editor page (`FormEditorPage`), and
- the `AddQuestionModal` used when creating a new question.

Layout matches the question card: a full-height gray rail on the right of each row with the same
six-dot grip icon, using the same CSS utility classes (extracted into a shared component).

### No backend work is required

Option `Order` already round-trips end to end:

| Concern | Where | Status |
|---|---|---|
| Order-only change detected as a change | `src/FormAI.Application/Forms/SaveFormEditor/FormEditorDiff.cs:114-117` — `IsUnchanged(QuestionOption, OptionInput)` compares `existing.Order == input.Order` | works |
| Order-only change persisted | `SaveFormEditorHandler.cs:100-102` — `option.Update(o.Text.Trim(), o.Order, o.IsCorrect)` for every `diff.Modified` | works |
| Order sent from the client | `frontend/src/api/forms.ts` `saveFormEditor` recomputes `order: oi + 1` from array position at request time | works |
| Order returned on reload | `GetFormHandler.cs:39` — `.OrderBy(o => o.Order)` | works |
| Order shown to respondents | `GetFormToAnswerHandler.cs:29` — `q.Options.OrderBy(o => o.Order)` | works |

Because `order` is derived from array index at save time, the client only has to reorder the
`FormOption[]` array. Nothing else. Option ids are untouched by a reorder, so `answer_selected_options`
history survives.

**Scope: frontend only.** Nothing under `src/FormAI.*` changes. `frontend/src/types/form.ts` does not
change either (`FormOption.id` already exists and is stable).

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/editors/DragHandleRail.tsx` | **new** — shared drag rail + exported class string |
| `frontend/src/components/editors/SortableOptionRow.tsx` | **new** — `useSortable` wrapper around `OptionRow` + rail |
| `frontend/src/components/editors/OptionList.tsx` | **new** — option state handlers + `DndContext`/`SortableContext` |
| `frontend/src/components/editors/OptionRow.tsx` | 1-line change: wrapper div becomes the row *content* |
| `frontend/src/components/editors/RadioButtonList.tsx` | replaced by a thin wrapper over `OptionList` |
| `frontend/src/components/editors/CheckBoxList.tsx` | replaced by a thin wrapper over `OptionList` |
| `frontend/src/components/editors/QuestionCard.tsx` | inline rail markup replaced with `<DragHandleRail />` |
| `frontend/src/components/editors/AddQuestionModal.tsx` | delete four dead option handlers |

`QuestionCard`'s and `AddQuestionModal`'s *usage* of `RadioButtonList` / `CheckBoxList` does **not**
change — the wrappers keep the exact same prop shape, so the modal gets reordering for free.

### TypeScript notes before you start

`frontend/tsconfig.app.json` has `"verbatimModuleSyntax": true`, so every type-only import **must**
use `import type { ... }`. It also has `"noUnusedLocals": true` and `"noUnusedParameters": true`, which
is why step 7 (deleting dead code) matters: `RadioButtonList.markCorrect` and the four unused handlers
in `AddQuestionModal` (one of which also has an unused `o` parameter) are exactly the kind of thing
those flags reject. If `npm run build` currently fails for you, that pre-existing dead code is why —
this phase removes it.

`"strict"` is **not** enabled, so `FormOption.isCorrect` (`boolean | null`) remains assignable to
`OptionRow`'s `isCorrect: boolean` prop, as it is today. No widening needed.

---

## Step 1 — `DragHandleRail.tsx` (new)

Lifts the rail currently inlined at `QuestionCard.tsx:158-169` so the question card and the option row
literally share the same classes. (The project is Tailwind v4 with no custom CSS classes — `index.css`
only defines `@theme` tokens — so the reusable unit is a component plus an exported class string.)

Create `frontend/src/components/editors/DragHandleRail.tsx`:

```tsx
import type { DraggableSyntheticListeners } from '@dnd-kit/core';

/** Shared drag-rail styling. Width is applied separately so rows can use a compact rail. */
export const DRAG_RAIL_CLASSES =
  'flex shrink-0 items-center justify-center border-l border-gray-200 ' +
  'bg-gray-50 text-gray-400 cursor-grab active:cursor-grabbing hover:bg-gray-100 hover:text-gray-600';

interface DragHandleRailProps {
  /** `listeners` from useSortable — attaches the drag activator to the rail. */
  listeners: DraggableSyntheticListeners;
  /** Narrower rail + smaller grip, for option rows. */
  compact?: boolean;
  title?: string;
}

export function DragHandleRail({
  listeners,
  compact = false,
  title = 'Drag to reorder'
}: DragHandleRailProps) {
  const size = compact ? 16 : 20;

  return (
    <div
      {...listeners}
      style={{ touchAction: 'none' }}
      title={title}
      className={`${DRAG_RAIL_CLASSES} ${compact ? 'w-8' : 'w-10'}`}
    >
      <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} fill="currentColor" viewBox="0 0 256 256">
        <path d="M108,60A16,16,0,1,1,92,44,16,16,0,0,1,108,60Zm56,0a16,16,0,1,0-16-16A16,16,0,0,0,164,60ZM92,112a16,16,0,1,0,16,16A16,16,0,0,0,92,112Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,112ZM92,180a16,16,0,1,0,16,16A16,16,0,0,0,92,180Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,180Z" />
      </svg>
    </div>
  );
}
```

`DraggableSyntheticListeners` is exported from the `@dnd-kit/core` package root (verified in
`node_modules/@dnd-kit/core/dist/index.d.ts`) — no deep import needed.

`touchAction: 'none'` must stay — there are no configured sensors anywhere in this project
(no `useSensor` / `useSensors`), so that inline style is what makes touch dragging work.

---

## Step 2 — `QuestionCard.tsx` (use the shared rail)

Two edits, no visual change.

**2a.** Add the import after line 8 (`import { CSS } from '@dnd-kit/utilities';`):

```tsx
import { DragHandleRail } from "./DragHandleRail";
```

**2b.** Replace the whole rail block — `QuestionCard.tsx:158-169`, i.e. from the comment
`{/* Drag handle rail, attached to the card via a divider */}` through the closing `</div>` of that
`<div {...listeners} …>` — with:

```tsx
      {/* Drag handle rail, attached to the card via a divider */}
      <DragHandleRail listeners={listeners} />
```

Leave everything else in the file alone: `useSortable` at `:41-43`, the `style` object at `:45-49`,
and the root `<div ref={setNodeRef} style={style} {...attributes} className="flex items-stretch overflow-hidden …">`
at `:106-107` all stay exactly as they are.

---

## Step 3 — `OptionRow.tsx` (become the row content)

Only the outer `<div>` at `OptionRow.tsx:36` changes. All inner markup and the draft/commit editing
logic stay byte-for-byte as they are.

Before:

```tsx
<div className={`flex items-center gap-2 py-1 px-2 rounded-md transition-colors ${isCorrect ? 'bg-green-50' : ''}`}>
```

After:

```tsx
<div className={`flex flex-1 min-w-0 items-center gap-2 py-1 px-2 transition-colors ${isCorrect ? 'bg-green-50' : ''}`}>
```

Why: `flex-1 min-w-0` makes the row the growing column next to the fixed-width rail (same relationship
as `QuestionCard`'s `flex-1 min-w-0 p-5` content div at `:108`). `rounded-md` moves up to the
`SortableOptionRow` wrapper so the green "correct" fill runs flush into the rail's divider instead of
leaving a rounded gap. The rail itself stays gray on correct rows — that is intentional and matches
the approved mockup.

`OptionRow`'s props and its `OptionRowProps` interface do not change.

---

## Step 4 — `SortableOptionRow.tsx` (new)

Mirrors `QuestionCard`'s sortable setup exactly — same hook destructuring, same `style` object,
same `attributes`-on-root / `listeners`-on-rail split.

Create `frontend/src/components/editors/SortableOptionRow.tsx`:

```tsx
import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import type { FormOption } from '../../types/form';
import { OptionRow } from './OptionRow';
import { DragHandleRail } from './DragHandleRail';

interface SortableOptionRowProps {
  option: FormOption;
  inputType: 'checkbox' | 'radio';
  questionId: string;
  onTextChange: (text: string) => void;
  onCorrectChange: (isCorrect: boolean) => void;
  onRemove: () => void;
}

export function SortableOptionRow({
  option,
  inputType,
  questionId,
  onTextChange,
  onCorrectChange,
  onRemove
}: SortableOptionRowProps) {

  const { attributes, listeners, setNodeRef,
    transform, transition, isDragging } =
    useSortable({ id: option.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <div ref={setNodeRef} style={style} {...attributes}
      className="flex items-stretch overflow-hidden rounded-md border border-gray-200 bg-white">

      <OptionRow
        text={option.text}
        isCorrect={option.isCorrect}
        inputType={inputType}
        questionId={questionId}
        onTextChange={onTextChange}
        onCorrectChange={onCorrectChange}
        onRemove={onRemove}
      />

      {/* Drag handle rail, attached to the row via a divider */}
      <DragHandleRail listeners={listeners} compact />
    </div>
  );
}
```

Resulting shape, per option:

```
┌──────────────────────────────────────┬────┐
│ ○  Paris          [Mark correct]  🗑 │ ⠿  │
└──────────────────────────────────────┴────┘
┌──────────────────────────────────────┬────┐
│ ●  Lisbon         [ Correct  ]    🗑 │ ⠿  │  ← content is bg-green-50
└──────────────────────────────────────┴────┘
```

The `key` stays on the parent's map (`key={opt.id}` in `OptionList`) and `useSortable({ id: option.id })`
uses the same id — this is what preserves `OptionRow`'s local `editing` / `draft` state across a reorder.

---

## Step 5 — `OptionList.tsx` (new) — the shared list that owns the DnD

`RadioButtonList.tsx` and `CheckBoxList.tsx` are near-duplicates today; they differ only in `inputType`
(plus `RadioButtonList.markCorrect` at `:14-18`, which is dead — never called, and it is *not* what the
"Mark correct" button uses). Consolidating means the drag wiring exists once.

Create `frontend/src/components/editors/OptionList.tsx`:

```tsx
import { DndContext, closestCenter } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';
import type { FormOption } from '../../types/form';
import { SortableOptionRow } from './SortableOptionRow';

interface OptionListProps {
  options: FormOption[];
  questionId: string;
  inputType: 'checkbox' | 'radio';
  onOptionsChange: (options: FormOption[]) => void;
}

export function OptionList({
  options, questionId, inputType, onOptionsChange
}: OptionListProps) {

  function updateOption(index: number,
    patch: Partial<FormOption>) {
    onOptionsChange(options.map((o, i) =>
      i === index ? { ...o, ...patch } : o));
  }

  function removeOption(index: number) {
    onOptionsChange(options.filter((_, i) => i !== index));
  }

  function addOption() {
    onOptionsChange([
      ...options,
      {
        id: crypto.randomUUID(), text: 'New option',
        order: options.length + 1, isCorrect: false
      }
    ])
  }

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = options.findIndex(o => o.id === active.id);
    const newIndex = options.findIndex(o => o.id === over.id);
    onOptionsChange(arrayMove(options, oldIndex, newIndex));
  }

  return (
    <div className="mt-3 space-y-1">
      <DndContext collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
        <SortableContext items={options.map(o => o.id)} strategy={verticalListSortingStrategy}>
          {options.map((opt, i) => (
            <SortableOptionRow
              key={opt.id}
              option={opt}
              inputType={inputType}
              questionId={questionId}
              onTextChange={(text) => updateOption(i, { text })}
              onCorrectChange={(isCorrect) => updateOption(i, { isCorrect })}
              onRemove={() => removeOption(i)}
            />
          ))}
        </SortableContext>
      </DndContext>

      <button
        onClick={addOption}
        className="mt-1 text-xs text-brand-600 hover:text-brand-800 hover:underline"
      >
        + Add option
      </button>
    </div>
  )
}
```

Points to keep in mind while typing this:

- `updateOption` / `removeOption` / `addOption` are copied **verbatim** from `RadioButtonList.tsx:20-38`.
  In particular `crypto.randomUUID()` must stay — new options need a client-minted id so the phase-14
  `FormEditorDiffer` can match them; and `order: options.length + 1` is cosmetic, since `saveFormEditor`
  renumbers from array index anyway.
- `handleDragEnd` mirrors `FormEditorPage.tsx:64-72`, but calls `onOptionsChange` instead of a state
  setter, because options live in the parent's state.
- `space-y-0.5` → `space-y-1`, so the now-bordered rows don't touch each other.
- Same `collisionDetection={closestCenter}` + `verticalListSortingStrategy` as the questions list.

### Why the `DndContext` lives inside `OptionList`

This is the load-bearing decision:

- In `AddQuestionModal` there is **no** surrounding `DndContext` — the modal is not part of the questions
  drag tree. Owning its own context is what makes reordering work there at all.
- In `QuestionCard` this nests inside the page-level questions `DndContext` (`FormEditorPage.tsx:277`).
  Nesting is safe here because the question's drag `listeners` are attached **only** to the question rail
  (`QuestionCard.tsx:159-160`), which is not an ancestor of any option row. The two sets of activator
  listeners never overlap, so dragging an option cannot start a question drag and vice versa.
  (`{...attributes}` on the card root only adds `role` / `tabIndex` / `aria-*` — no pointer handlers.)

---

## Step 6 — thin wrappers, so no call site changes

Replace the **entire contents** of `frontend/src/components/editors/RadioButtonList.tsx` with:

```tsx
import type { FormOption } from '../../types/form';
import { OptionList } from './OptionList';

interface RadioButtonListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function RadioButtonList(props: RadioButtonListProps) {
  return <OptionList {...props} inputType="radio" />;
}
```

Replace the **entire contents** of `frontend/src/components/editors/CheckBoxList.tsx` with:

```tsx
import type { FormOption } from '../../types/form';
import { OptionList } from './OptionList';

interface CheckBoxListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function CheckBoxList(props: CheckBoxListProps) {
  return <OptionList {...props} inputType="checkbox" />;
}
```

Both keep the same named export and the same prop shape, so **`QuestionCard.tsx:64-103` and
`AddQuestionModal.tsx:114-134` need no edits at all.**

The dead `markCorrect` from `RadioButtonList.tsx:14-18` is gone. Behaviour is unchanged by that removal
(it was never called), but note the pre-existing quirk it hints at: a `Single` question can still have
more than one option flagged `isCorrect`, because the "Mark correct" button routes through
`onCorrectChange` → `updateOption(i, { isCorrect })`, which patches only that one option. **Out of scope
for this phase** — do not fix it here; it is a separate behavioural change.

---

## Step 7 — `AddQuestionModal.tsx` (delete dead code)

Delete `AddQuestionModal.tsx:33-47` in full — these four functions are superseded by the list
components (the JSX at `:120-132` passes `onOptionsChange={setOptions}` and the list does the work).
None of them is referenced anywhere:

```tsx
  function updateOptionText(index: number, value: string) { … }
  function toggleCorrect(index: number) { … }
  function addOption() { … }
  function removeOption(index: number) { … }
```

Keep everything else: `makeOption()` at `:21-23`, the `useState` block at `:26-29`, `hasOptions` at `:31`,
`handleSubmit` at `:49-79`, and all the JSX.

`handleSubmit` already renumbers from array position at `:76`:

```tsx
options: hasOptions ? options.map((o, i) => ({ ...o, order: i + 1 })) : []
```

so whatever order the user dragged the options into in the modal is the order the new question is
created with. No change needed there.

---

## Verification

### 1. Type-check / build

```bash
cd frontend
npm run build     # tsc -b && vite build
```

Must pass. The `OptionRow` prop plumbing through `SortableOptionRow` and the `import type` requirements
(`verbatimModuleSyntax`) are what this catches. If it reports unused locals, re-check step 7.

### 2. Run the app

```bash
dotnet run --project src/FormAI.API      # http://localhost:5155
cd frontend && npm run dev
```

### 3. Manual checks — form editor (`FormEditorPage`)

- Open a form with a **Single choice** question. Each option now shows a bordered row with a gray rail on
  the right. Drag an option by its rail → the option moves; the question itself does **not** move.
- Repeat on a **Multiple choice** question.
- Drag a **question** by its (unchanged, `w-10`) rail → questions still reorder normally. This confirms
  the nested `DndContext` did not break the outer one.
- Click an option's text to edit it, and click the "Mark correct" pill — both still work after a reorder
  (state survives because `key` and the sortable id are both `option.id`).
- **Persistence:** reorder options → Save → hard-reload the page. The new order must be there.
  This exercises `FormEditorDiff.cs:114-117` classifying an order-only change as `Modified`.
- **History:** on a form that already has submissions, reorder options → Save → open the submission
  results. Answers must still show the correct option text. (Reordering must not change any option id.)

### 4. Manual checks — `AddQuestionModal`

- Click "Add question", pick **Single choice**. The two default blank options each have a rail; drag to
  swap them, add a third with "+ Add option", drag it to the top, then submit.
- The created question's options must appear in the dragged order. Save the form and reload to confirm
  the order persisted (this path goes through `diff.Added` → `BuildQuestion` →
  `QuestionOption.Create(…, o.Order, …)`).
- Repeat with **Multiple choice**.

### 5. Respondent view

Open the public respond page for the form (`GetFormToAnswerHandler` path) and confirm options render in
the new order.

### 6. Backend regression

```bash
dotnet test FormAI.sln
```

Should be unaffected — no backend files change — but run it to confirm.
