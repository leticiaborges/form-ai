# Phase 17 — Tab control on the form editor page

## Context

`FormEditorPage` is one long screen: a sticky header (title input, Back, Save, Delete) and a `<main>`
holding the description, the two flags, the question list and the "Add question" button. Phases 18–23
add a second, read-only view of the same form — the **Results** tab — so this phase carves the page
into two tab panels and adds nothing else.

This phase is deliberately boring. It ships **no new behaviour**: after it, the editor works exactly
as it does today, and the Results tab is a placeholder. That is the point — if anything about the
editor breaks, it broke here, and there are no charts in the way to obscure it.

### Vocabulary

The second tab is the **Results** tab. Not "submissions", not "analytics", not "statistics". Phase 18
adds the term to `CONTEXT.md`; this phase just uses it.

### Three decisions baked into this phase

**1. Tab state lives in the URL (`?tab=results`), not in `useState`.**
`react-router-dom` v7 is already a dependency, so `useSearchParams` costs one import. A reload or a
copied link keeps you on the tab you were on. The `editor` tab is the default and is represented by
*no* query param at all, so `/forms/{id}` keeps working untouched.

**2. All editor state stays in `FormEditorPage`.**
`FormEditorTab` is presentational: it receives `questions`, `description`, `isPublic` and `isGraded`
as props and reports changes upward. This is what guarantees that editing three questions, peeking at
Results, and switching back **does not lose the edits** — the tab panel unmounts, the state does not.
It is also what lets the header's Save button reach the state from outside both tabs.

**3. The sticky header does not change.**
Title input, Back, Save and Delete stay exactly where they are. They apply to the whole form, not to
one tab, so they sit above the tab strip. The title stays editable from both tabs, which is
consistent with Save being reachable from both.

**Scope: frontend only.** Nothing under `src/FormAI.*` changes. No new npm dependency — Recharts
arrives in phase 20.

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/Tabs.tsx` | **new** — generic controlled tab strip |
| `frontend/src/components/results/ResultsTab.tsx` | **new** — placeholder panel |
| `frontend/src/components/editors/FormEditorTab.tsx` | **new** — everything currently inside `<main>` |
| `frontend/src/pages/FormEditorPage.tsx` | `<main>` body replaced by the tab strip + panel switch |

### TypeScript notes before you start

`frontend/tsconfig.app.json` has `"verbatimModuleSyntax": true` — every type-only import **must** use
`import type { ... }`. It also has `"noUnusedLocals"` and `"noUnusedParameters"`, so when you move code
out of `FormEditorPage` you must also delete the now-unused imports (`QuestionCard`,
`AddQuestionModal`, `DndContext`, `SortableContext`, `arrayMove`, and the `DragEndEvent` type).
If `npm run build` fails after step 4, unused imports are the first place to look.

`"strict"` is **not** enabled, so `null` remains assignable where you would otherwise need a widening.

---

## Step 1 — `Tabs.tsx` (new)

A controlled component: it renders buttons and reports clicks. It does not know about URLs, forms or
results, so it can be reused anywhere later.

Create `frontend/src/components/Tabs.tsx`:

```tsx
export interface TabDefinition {
    id: string;
    label: string;
}

interface TabsProps {
    tabs: TabDefinition[];
    activeTab: string;
    onTabChange: (id: string) => void;
}

export function Tabs({ tabs, activeTab, onTabChange }: Readonly<TabsProps>) {
    return (
        <div role="tablist" className="flex items-center gap-1 border-b border-gray-200">
            {tabs.map(tab => {
                const isActive = tab.id === activeTab;

                return (
                    <button
                        key={tab.id}
                        role="tab"
                        type="button"
                        aria-selected={isActive}
                        onClick={() => onTabChange(tab.id)}
                        className={
                            'px-4 py-2 text-sm font-medium transition-colors border-b-2 -mb-px ' +
                            (isActive
                                ? 'border-brand-600 text-brand-700'
                                : 'border-transparent text-gray-500 hover:text-gray-800 hover:border-gray-300')
                        }
                    >
                        {tab.label}
                    </button>
                );
            })}
        </div>
    );
}
```

`-mb-px` pulls the active tab's 2px underline over the strip's own 1px border so they merge into one
line. `Readonly<Props>` matches the convention already used by `SubmissionsPerFormChart`.

---

## Step 2 — `ResultsTab.tsx` (new, placeholder)

Create `frontend/src/components/results/ResultsTab.tsx` — a new `results/` folder alongside
`editors/` and `respond/`, which is where phases 19–23 add their components:

```tsx
interface ResultsTabProps {
    formId: string;
}

export function ResultsTab({ formId }: Readonly<ResultsTabProps>) {
    return (
        <div className="py-16 text-center text-gray-400">
            <p className="text-lg">Results are coming in phase 19.</p>
            <p className="mt-1 text-sm">Form {formId}</p>
        </div>
    );
}
```

`formId` is referenced in the markup on purpose, so `noUnusedParameters` stays quiet. Phase 19
replaces this file wholesale.

---

## Step 3 — `FormEditorTab.tsx` (new)

This is a **move, not a rewrite.** Take everything currently inside `FormEditorPage`'s `<main>` —
the empty-question message, the description textarea, the Public/Graded checkboxes, the share-link
pill, the `DndContext`/`SortableContext` question list, the "+ Add question" button and the
`AddQuestionModal` — and put it here unchanged. Only the *state plumbing* changes.

What moves **into** this component (it owns them):

- `showAddModal` — modal visibility is a concern of the editor alone
- `updateQuestion`, `removeQuestion`, `handleDragEnd`, `appendQuestion` — they now call
  `onQuestionsChange(...)` instead of `setQuestions(...)`
- `handleCopyLink` — a local UI concern

What stays **in the page** and arrives as props: `questions`, `description`, `descriptionError`,
`isPublic`, `isGraded`, and the `form` itself (for the share link).

Create `frontend/src/components/editors/FormEditorTab.tsx`:

```tsx
import { useState } from 'react';
import { DndContext, closestCenter } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';
import type { FormDetail, FormQuestion } from '../../types/form';
import { QuestionCard } from './QuestionCard';
import { AddQuestionModal } from './AddQuestionModal';
import { showSuccess, showError } from '../../utils/toast';

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
    onIsGradedChange
}: Readonly<FormEditorTabProps>) {

    const [showAddModal, setShowAddModal] = useState(false);

    function updateQuestion(index: number, updated: FormQuestion) {
        onQuestionsChange(questions.map((q, i) => i === index ? updated : q));
    }

    function removeQuestion(index: number) {
        onQuestionsChange(questions.filter((_, i) => i !== index));
    }

    function appendQuestion(question: FormQuestion) {
        onQuestionsChange([...questions, question]);
    }

    function handleDragEnd(event: DragEndEvent) {
        const { active, over } = event;
        if (!over || active.id === over.id) return;

        const oldIndex = questions.findIndex(q => q.id === active.id);
        const newIndex = questions.findIndex(q => q.id === over.id);
        onQuestionsChange(arrayMove(questions, oldIndex, newIndex));
    }

    async function handleCopyLink(url: string) {
        try {
            await navigator.clipboard.writeText(url);
            showSuccess('Link copied to clipboard.');
        } catch {
            showError('Could not copy link. Please copy it manually.');
        }
    }

    return (
        <div className="flex flex-col gap-4">
            {/* ...paste the existing <main> children here, unchanged apart from the handlers below... */}
        </div>
    );
}
```

When pasting the body, exactly four call sites change:

| Was (in `FormEditorPage`) | Becomes (in `FormEditorTab`) |
|---|---|
| `setIsPublic(e.target.checked)` | `onIsPublicChange(e.target.checked)` |
| `toggleGraded(e.target.checked)` | `onIsGradedChange(e.target.checked)` |
| `setDescription(...)` then `setDescriptionError('')` | `onDescriptionChange(e.target.value)` |
| `setQuestions(...)` inside the four helpers | `onQuestionsChange(...)` |

Everything else — the class strings, the SVG icons, the share-link block guarded by `form.isPublic`,
the `<AddQuestionModal>` at the bottom — is copied verbatim. The outer wrapper changes from
`<main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">` to the plain
`<div className="flex flex-col gap-4">` above, because the page now owns the `<main>` and its width.

---

## Step 4 — `FormEditorPage.tsx`

**4a. Imports.** Add:

```tsx
import { useSearchParams } from 'react-router-dom';
import { Tabs } from '../components/Tabs';
import type { TabDefinition } from '../components/Tabs';
import { FormEditorTab } from '../components/editors/FormEditorTab';
import { ResultsTab } from '../components/results/ResultsTab';
```

Delete the imports that moved to `FormEditorTab`: `QuestionCard`, `AddQuestionModal`,
`DndContext` / `closestCenter`, the `DragEndEvent` type, and
`SortableContext` / `verticalListSortingStrategy` / `arrayMove`.

Keep `DEFAULT_POINTS` — `toggleGraded` still lives here. Keep `showSuccess` / `showError` and
`getErrorMessage`: `handleSave` and `handleDelete` use them.

**4b. Delete the moved functions** from the page: `updateQuestion`, `removeQuestion`,
`handleDragEnd`, `appendQuestion`, `handleCopyLink`, and the `showAddModal` state. `toggleGraded`
**stays** — it is the `onIsGradedChange` handler, and its default-points behaviour is unchanged.

**4c. Add the tab plumbing** next to the other `useState` calls:

```tsx
const [searchParams, setSearchParams] = useSearchParams();

const TABS: TabDefinition[] = [
    { id: 'editor', label: 'Edit form' },
    { id: 'results', label: 'Results' }
];

const activeTab = searchParams.get('tab') === 'results' ? 'results' : 'editor';

function changeTab(id: string) {
    // `replace` keeps tab switching out of the history stack, so Back returns to the
    // dashboard rather than walking through every tab the owner clicked.
    setSearchParams(id === 'editor' ? {} : { tab: id }, { replace: true });
}
```

Any unrecognised `?tab=` value falls back to `editor`, so a mistyped URL never renders a blank page.

**4d. Replace the whole `<main>`** — from `<main className="mx-auto max-w-2xl px-4 py-8 ...">` down to
its closing `</main>` — with:

```tsx
<main className="mx-auto w-full max-w-2xl px-4 py-8 flex flex-col gap-6">
    <Tabs tabs={TABS} activeTab={activeTab} onTabChange={changeTab} />

    {activeTab === 'editor' ? (
        <FormEditorTab
            form={form}
            questions={questions}
            onQuestionsChange={setQuestions}
            description={description}
            onDescriptionChange={value => { setDescription(value); setDescriptionError(''); }}
            descriptionError={descriptionError}
            isPublic={isPublic}
            onIsPublicChange={setIsPublic}
            isGraded={isGraded}
            onIsGradedChange={toggleGraded}
        />
    ) : (
        <ResultsTab formId={form.id} />
    )}
</main>
```

`onQuestionsChange={setQuestions}` works directly because `FormEditorTab` always passes a finished
array, never an updater function.

**4e. Leave the sticky `<header>` completely alone**, and leave the `showDeleteModal` block at the
bottom of the page alone — Delete is a whole-form action and belongs outside both tabs.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build     # tsc -b && vite build
```

Must pass. Unused imports left behind in `FormEditorPage` are what this catches.

### 2. Run

```bash
dotnet run --project src/FormAI.API      # http://localhost:5155
cd frontend && npm run dev               # http://localhost:5173
```

### 3. Manual checks — the editor must behave exactly as before

- Open a form. Two tabs appear above the description; **Edit form** is active and the URL has no
  `?tab=`.
- Edit a question, drag to reorder, add an option, delete a question, tick Public and Graded, edit the
  description — all identical to before this phase.
- Ticking **Graded form** still fills empty points with the default. This proves `toggleGraded`
  survived the move.
- Save. The toast appears and the form reloads. Delete opens the modal with the submission count.

### 4. Manual checks — the tabs

- Click **Results** → URL becomes `/forms/{id}?tab=results`, the placeholder shows, and the sticky
  header (title, Back, Save, Delete) is still there.
- **The state test, and the reason this phase exists:** on **Edit form**, retype a question and change
  the description *without saving* → switch to **Results** → switch back. **Both edits must still be
  there.** If they are gone, `FormEditorTab` is holding state it should have received as props.
- Reload the page while on `?tab=results` → you land on Results, not the editor.
- Hand-edit the URL to `?tab=nonsense` → the editor renders.
- Press Back from either tab → you go to wherever you came from, not through tab history.

### 5. Backend

```bash
dotnet test FormAI.sln
```

Unaffected — no backend file changes — but run it to confirm.
