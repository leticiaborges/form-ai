# Phase 19 — Wiring the Results tab to the endpoint

## Context

Phase 18 built `GET /api/forms/{id}/results`. Phase 17 left a placeholder panel. This phase joins
them: types, an API client, and a real `ResultsTab` that fetches, handles loading / error / empty, and
renders one card per question showing the question and its answer count.

**There are still no charts here.** That is on purpose. If the DTO from phase 18 is wrong — a field
named differently, a count off by one, an option list that comes back empty — you find out from a card
that reads `4 answers`, not from a chart library that silently renders nothing. Phase 20 introduces
Recharts against a payload you have already seen working.

To make this phase verifiable end to end, each card renders a plain temporary list of its options or
values with raw counts. Phase 20 replaces the option list with a chart; phase 21 replaces the value
list with the real one. Both are marked in the code as temporary.

### What this phase settles

**The answer count is the denominator.** A card reads `4 answers`, meaning four people answered *this
question* — not four of the form's submissions. On a form with 6 submissions where 2 skipped, the card
says `4 answers` while the tab header says `6 submissions`. That is the rule from phase 18, and it is
the first time it is visible.

**Zero is a sentence, not a number.** A question nobody answered reads `No answers yet`, never
`0 answers`. Every percentage in phases 20–23 must guard its divisor, because that card's divisor is
zero.

**Saving from the Results tab refetches it.** Save lives in the sticky header and is reachable from
both tabs, and a save can rescore ([ADR 0004](../adr/0004-scores-recomputed-from-current-form.md)).
Switching *to* the tab already refetches, because the panel unmounts when you switch away. The case
that needs handling is saving *while standing on* the Results tab, which is what `reloadKey` below is
for.

**Scope: frontend only.** No backend file changes, no new npm dependency.

---

## Files

| File | Change |
|---|---|
| `frontend/src/types/results.ts` | **new** — mirrors `GetFormResultsResponse` |
| `frontend/src/api/results.ts` | **new** — `getFormResults()` |
| `frontend/src/components/results/QuestionResultCard.tsx` | **new** — one card per question |
| `frontend/src/components/results/ResultsTab.tsx` | replaced — the real panel |
| `frontend/src/pages/FormEditorPage.tsx` | add `resultsReloadKey`, bump it on save |

---

## Step 1 — `types/results.ts` (new)

Field-for-field with `GetFormResultsResponse`, camelCased. `QuestionType` is reused from
`types/form.ts` — the API serialises the enum as a string (`JsonStringEnumConverter` in `Program.cs`),
which is why `'Single' | 'Multiple' | 'Text' | 'Numeric'` already works there.

Create `frontend/src/types/results.ts`:

```ts
import type { QuestionType } from './form';

export interface OptionResult {
    text: string;
    count: number;
    /** Null on an ungraded form, on an unmarked option, and on an option that no longer exists. */
    isCorrect: boolean | null;
}

export interface ValueResult {
    value: string;
    count: number;
    isCorrect: boolean | null;
}

export interface ScoreBucket {
    score: number;
    submissionCount: number;
}

export interface QuestionResult {
    questionId: string;
    text: string;
    type: QuestionType;
    order: number;
    /** Answers to this question. Lower than the form's submission count when people skipped it. */
    answerCount: number;
    points: number | null;
    /** Null on an ungraded form. */
    correctAnswerCount: number | null;
    /** Populated for Single and Multiple; empty otherwise. */
    options: OptionResult[];
    /** Populated for Text and Numeric; empty otherwise. */
    values: ValueResult[];
}

export interface FormResults {
    formId: string;
    title: string;
    isGraded: boolean;
    submissionCount: number;
    /** Null on an ungraded form. */
    totalPoints: number | null;
    /** Empty on an ungraded form. */
    scoreDistribution: ScoreBucket[];
    questions: QuestionResult[];
}
```

`import type` is mandatory — `tsconfig.app.json` sets `verbatimModuleSyntax: true`.

---

## Step 2 — `api/results.ts` (new)

Create `frontend/src/api/results.ts`, following the shape of `api/forms.ts`:

```ts
import api from './axios';
import type { FormResults } from '../types/results';

export async function getFormResults(formId: string): Promise<FormResults> {
    const response = await api.get<FormResults>(`/forms/${formId}/results`);
    return response.data;
}
```

The shared `api` instance already attaches the bearer token, which this endpoint requires.

---

## Step 3 — `QuestionResultCard.tsx` (new)

Order inside the card, as specified: the question first, the answer count second, the distribution
last.

Create `frontend/src/components/results/QuestionResultCard.tsx`:

```tsx
import type { QuestionResult } from '../../types/results';

interface QuestionResultCardProps {
    question: QuestionResult;
}

export function QuestionResultCard({ question }: Readonly<QuestionResultCardProps>) {
    const { answerCount } = question;

    const isSelection = question.type === 'Single' || question.type === 'Multiple';

    return (
        <section className="bg-white rounded-2xl shadow-md p-6 flex flex-col gap-3">
            <h3 className="text-base font-medium text-gray-900">{question.text}</h3>

            <p className="text-xs text-gray-500">
                {answerCount === 0
                    ? 'No answers yet'
                    : `${answerCount} answer${answerCount !== 1 ? 's' : ''}`}
            </p>

            {/* Temporary: phase 20 replaces this with a bar chart, phase 21 with the value list. */}
            {answerCount > 0 && (
                <ul className="flex flex-col gap-1 text-sm text-gray-600">
                    {isSelection
                        ? question.options.map(option => (
                            <li key={option.text}>{option.text} — {option.count}</li>
                        ))
                        : question.values.map(value => (
                            <li key={value.value}>{value.value} — {value.count}</li>
                        ))}
                </ul>
            )}
        </section>
    );
}
```

Two details that matter later:

- The card renders `question.options` for Single/Multiple and `question.values` otherwise. Phase 18
  guarantees exactly one of the two is populated, so nothing needs a fallback.
- `key={option.text}` is safe: option text is unique within a question, trimmed and case-insensitively
  (`QuestionOptionValidator`), and phase 18's grouping uses the same comparison — so no two entries in
  this list can collide.
- An option nobody picked still appears, with `count: 0`. That is deliberate: the owner needs to see
  the option that nobody chose.

---

## Step 4 — `ResultsTab.tsx` (replace the placeholder)

Replace the whole contents of `frontend/src/components/results/ResultsTab.tsx`:

```tsx
import { useEffect, useState } from 'react';
import type { FormResults } from '../../types/results';
import { getFormResults } from '../../api/results';
import { QuestionResultCard } from './QuestionResultCard';

type ResultsState = 'loading' | 'ready' | 'error';

interface ResultsTabProps {
    formId: string;
    /**
     * Bumped by FormEditorPage after a successful save. Switching to this tab already refetches,
     * because the panel unmounts when you switch away; this covers saving while standing here,
     * where a rescore can have changed every score under you.
     */
    reloadKey: number;
}

export function ResultsTab({ formId, reloadKey }: Readonly<ResultsTabProps>) {
    const [state, setState] = useState<ResultsState>('loading');
    const [results, setResults] = useState<FormResults | null>(null);

    useEffect(() => {
        let cancelled = false;
        setState('loading');

        getFormResults(formId)
            .then(data => {
                if (cancelled) return;
                setResults(data);
                setState('ready');
            })
            .catch(() => {
                if (cancelled) return;
                setState('error');
            });

        return () => { cancelled = true; };
    }, [formId, reloadKey]);

    if (state === 'loading')
        return <p className="py-16 text-center text-gray-500">Loading results…</p>;

    if (state === 'error' || !results)
        return <p className="py-16 text-center text-red-600">Could not load the results.</p>;

    if (results.submissionCount === 0) {
        return (
            <div className="py-16 text-center text-gray-400">
                <p className="text-lg">No submissions yet.</p>
                <p className="mt-1 text-sm">Share the link and answers will show up here.</p>
            </div>
        );
    }

    return (
        <div className="flex flex-col gap-4">
            <p className="text-sm text-gray-500">
                {results.submissionCount} submission{results.submissionCount !== 1 ? 's' : ''}
            </p>

            {results.questions.map(question => (
                <QuestionResultCard key={question.questionId} question={question} />
            ))}
        </div>
    );
}
```

The `cancelled` flag is what stops a slow response from a previous `formId`/`reloadKey` landing after
a newer one and overwriting it. Without it, saving twice quickly can leave the older payload on screen.

The empty state short-circuits the whole tab — no question cards, and in phase 23 no score
distribution either. A form with no submissions has nothing to distribute.

---

## Step 5 — `FormEditorPage.tsx`

**5a.** Add the reload key next to the other `useState` calls:

```tsx
const [resultsReloadKey, setResultsReloadKey] = useState(0);
```

**5b.** In `handleSave`, in the success path — right after `setFormData(refreshedForm)` and before the
toast:

```tsx
            // A save can rescore every submission (ADR 0004), so anything the Results tab is
            // currently showing is stale.
            setResultsReloadKey(key => key + 1);
```

**5c.** Pass it down:

```tsx
        <ResultsTab formId={form.id} reloadKey={resultsReloadKey} />
```

Nothing else on the page changes.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build
```

### 2. Run

```bash
dotnet run --project src/FormAI.API
cd frontend && npm run dev
```

### 3. Manual checks — the happy path

Use a form that already has a few submissions; if you have none, open the answer link in two private
windows and submit twice.

- Open the form, click **Results**. You see `N submissions` and one card per question, in the same
  order as the editor.
- Each Single/Multiple card lists every current option with a count, **including options nobody
  picked** (count 0).
- Each Text/Numeric card lists the distinct answers with counts.
- Submit `paris` from one window and `PARIS` from another → **one** entry with count 2. This is
  phase 18's case-insensitive grouping arriving intact.

### 4. Manual checks — the states

- **A form with no submissions** → "No submissions yet.", no cards.
- **A question everyone skipped** (make one optional and skip it) → the card reads `No answers yet`
  and shows no list.
- **A question some people skipped** → its answer count is lower than the tab's submission count.
  This is the denominator rule; it is correct.
- **Error state:** stop the API and switch to the Results tab → "Could not load the results."
  The editor tab must still render.

### 5. Manual checks — refresh on save

- Stand on the **Results** tab. In the sticky header, change the title and click **Save**. The results
  reload (watch the network tab: a second `GET /results` fires).
- On a **graded** form, go to the editor, change an option's answer key, save, then switch to Results.
  The correct counts reflect the new key. (They are not rendered until phase 22 — check the network
  response for now.)
- Switch to Results, back to the editor, and back to Results → a fresh request each time. That is the
  unmount doing the invalidation for free.

### 6. Backend

```bash
dotnet test FormAI.sln
```

Unaffected — no backend file changes.
