# Phase 21 — Text and Numeric answer lists (ungraded)

## Context

Text and Numeric questions still render phase 19's temporary `value — count` list. This phase replaces
it with the real one: **a list, not a chart**, one row per distinct answer, with the number of
respondents who gave that exact answer shown beside it.

A chart is the wrong shape here. Free text has no fixed set of categories — a question answered by 40
people can have 38 distinct answers, and 38 bars of length 1 tell the owner nothing. A list ordered by
how often each answer was given puts the repeated answers at the top, which is the only signal there
is.

### What "the same answer" means

Phase 18 already did the grouping, so this phase renders what it is given. Worth knowing what that is:
answers are grouped **trimmed and case-insensitively**, and numbers by their value, so `paris`,
`Paris` and ` PARIS ` are one row with a count of 3, and `5` and `5.0` are one row with a count of 2.
The casing shown is whichever was submitted first.

That grouping deliberately matches `SubmissionScorer`. If the list split `paris` from `Paris`, phase
22's `1/6 correct` would contradict a list showing two separate wrong-looking rows.

**Scope: frontend only.** No backend changes, no new dependency.

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/results/ValueAnswerList.tsx` | **new** |
| `frontend/src/components/results/QuestionResultCard.tsx` | values branch renders the list |

---

## Step 1 — `ValueAnswerList.tsx` (new)

Create `frontend/src/components/results/ValueAnswerList.tsx`:

```tsx
import type { ValueResult } from '../../types/results';

/** Past this many distinct answers the list scrolls inside the card rather than growing forever. */
const SCROLL_AFTER = 8;

interface ValueAnswerListProps {
    values: ValueResult[];
}

export function ValueAnswerList({ values }: Readonly<ValueAnswerListProps>) {
    const scrolls = values.length > SCROLL_AFTER;

    return (
        <ul
            className={
                'flex flex-col divide-y divide-gray-100 rounded-lg border border-gray-100 ' +
                (scrolls ? 'max-h-72 overflow-y-auto' : '')
            }
        >
            {values.map(value => (
                <li
                    key={value.value}
                    className="flex items-center justify-between gap-3 px-3 py-2"
                >
                    <span className="min-w-0 break-words text-sm text-gray-700">
                        {value.value}
                    </span>
                    <span
                        className="shrink-0 text-xs tabular-nums text-gray-400"
                        title={`${value.count} respondent${value.count !== 1 ? 's' : ''} gave this answer`}
                    >
                        ({value.count})
                    </span>
                </li>
            ))}
        </ul>
    );
}
```

Points worth understanding:

- **The order is the backend's**, already sorted by count descending then alphabetically. Do not
  re-sort here — the frontend would be inventing a second ordering rule.
- `key={value.value}` is safe because phase 18 groups by that same trimmed, case-insensitive key, so
  no two rows can collide.
- `break-words` matters: a respondent can paste a paragraph into a Text question, and without it the
  card scrolls sideways.
- `tabular-nums` keeps the `(1)` / `(12)` counts aligned down the right edge.
- The count is shown on **every** row, including `(1)`. That is the spec. If it reads noisy once you
  see it with real data, `{value.count > 1 && ...}` is a one-line change — but decide that after
  looking, not before.
- `values` already carries an `isCorrect` field. **Ignore it here.** Phase 22 renders it.

---

## Step 2 — `QuestionResultCard.tsx`

**2a.** Add the import:

```tsx
import { ValueAnswerList } from './ValueAnswerList';
```

**2b.** Replace the temporary `<ul>` in the values branch, so the block from phase 20 becomes:

```tsx
            {answerCount > 0 && (
                isSelection
                    ? <OptionBarChart options={question.options} answerCount={answerCount} />
                    : <ValueAnswerList values={question.values} />
            )}
```

That removes the last of the temporary markup introduced in phase 19. Nothing else changes.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build
```

### 2. Set up data worth looking at

On a form with a Text question and a Numeric question, submit from several private windows:

| Question | Submissions |
|---|---|
| Text | `paris`, `Paris`, ` PARIS `, `lisbon` |
| Numeric | `5`, `5.0`, `7` |

### 3. Manual checks

- The Text question shows **two** rows: `paris (3)` and `lisbon (1)`, in that order. Three separate
  rows means the grouping in phase 18 is not doing what it should — fix it there, not here.
- The displayed casing is `paris` — the first one submitted, not the last, and not uppercased.
- The Numeric question shows `5 (2)` and `7 (1)`.
- The card header still reads `4 answers` for the Text question — **four answers, two rows.** The
  header counts answers; the list counts distinct answers. Those are different numbers and both are
  right.
- A question everyone skipped still reads `No answers yet` with no list.
- A Numeric question where somebody entered a decimal shows it with a dot (`2.5`), regardless of your
  machine's locale — phase 18 formats with `InvariantCulture`.

### 4. Long content

- Paste a 300-character paragraph as a Text answer → the row wraps, the count stays pinned right, and
  the card does not scroll horizontally.
- Submit more than 8 distinct answers → the list scrolls inside the card instead of stretching the
  page.

### 5. Nothing else regressed

- Single and Multiple questions still show phase 20's bar charts.
- Graded forms look the same as ungraded ones for now. Phase 22 changes that.
