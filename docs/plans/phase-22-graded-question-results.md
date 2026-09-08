# Phase 22 — Graded results, per question

## Context

Phases 19–21 render the same cards for every form, graded or not. This phase makes a graded form look
graded, one question at a time:

- the option in the answer key gets a **green** bar instead of the shared blue one;
- the card reads **`1/4 correct`** beside its answer count;
- a Text or Numeric answer matching the suggested answer gets a green row.

The form-level score distribution is phase 23. This phase changes nothing above the question cards.

### No backend work

Phase 18 already returns everything needed. On an **ungraded** form the backend sends
`correctAnswerCount: null` and `isCorrect: null` on every option and value, so the components below
can branch on the data alone — **no `isGraded` prop is needed anywhere.** `isCorrect === true` means
"green"; `null` and `false` both mean "not green", which is also what makes an option that no longer
exists render as an ordinary bar.

### Two things that will look like bugs and are not

**1. A green bar with a correct count of zero.** On a **Multiple** question the answer key might be
A + B. An option is green because it is *in* the key. An answer counts as correct only when the whole
selection equals the key exactly — `SubmissionScorer` is all-or-nothing, there is no partial credit.
So everyone can have picked green option A, and `0/6 correct` is still right.

**2. `1/4 correct` on a form with 6 submissions.** The denominator is the answers to *that question*,
not the form's submissions (phase 18). Two people skipped it.

### One thing that is uncomfortable and also not a bug

If the owner renames an option that was marked correct, the old recorded text appears as an ordinary
grey-blue bar and everyone who picked it counts as wrong. That is not a display problem — it is what
[ADR 0004](../adr/0004-scores-recomputed-from-current-form.md) already did to their stored scores, made
visible for the first time. `docs/known-gaps.md` lists it as deliberate and unwarned.

**Scope: frontend only.**

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/results/OptionBarChart.tsx` | per-bar colour via `<Cell>`, tick marker |
| `frontend/src/components/results/QuestionResultCard.tsx` | the correct-count line |
| `frontend/src/components/results/ValueAnswerList.tsx` | green row for a matching answer |
| `docs/known-gaps.md` | amend the "no answer key" row |

### One loose end, deliberately left

`QuestionResult.points` arrives from phase 18 and **nothing renders it**. That is on purpose: showing
what each question is worth was not part of the spec, and phase 23 takes the form's total from
`totalPoints`, not by summing this field. If a graded card ever feels like it is missing something,
`{question.points} points` next to the correct-count line is the whole change — but add it because you
decided to, not because the field was sitting there.

---

## Step 1 — `OptionBarChart.tsx`

Three edits. The component keeps exactly the same props.

**1a.** Add `Cell` to the Recharts import and a second colour constant beside `BAR_COLOR`:

```tsx
import {
    Bar, BarChart, Cell, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis
} from 'recharts';
```

```tsx
/** An option in the answer key. Only ever set on a graded form. */
const CORRECT_COLOR = '#16a34a';   // green-600
```

**1b.** Carry correctness into the chart data, and mark it in the label as well as the colour:

```tsx
    const data = options.map(option => {
        const percentage = answerCount === 0
            ? 0
            : Math.round((option.count / answerCount) * 100);

        const isCorrect = option.isCorrect === true;

        return {
            // The tick is what makes the answer key readable without relying on colour alone.
            text: isCorrect ? `${option.text} ✓` : option.text,
            count: option.count,
            isCorrect,
            label: `${option.count} (${percentage}%)`
        };
    });
```

`option.isCorrect === true` is the right test, not `option.isCorrect`. `null` means "not marked" and
`false` means "marked wrong"; neither is green, and an option that no longer exists on the question is
always `null`.

**1c.** Give the `<Bar>` one `<Cell>` per row. `fill` on the `<Bar>` stays as the default for anything
a `<Cell>` does not override, so leave it:

```tsx
                <Bar dataKey="count" fill={BAR_COLOR} radius={[0, 4, 4, 0]} barSize={18}>
                    {data.map(entry => (
                        <Cell
                            key={entry.text}
                            fill={entry.isCorrect ? CORRECT_COLOR : BAR_COLOR}
                        />
                    ))}
                    <LabelList
                        dataKey="label"
                        position="right"
                        style={{ fontSize: 12, fill: '#374151' }}
                    />
                </Bar>
```

`<Cell>` children map positionally onto the data rows, so their order must match `data` exactly —
which it does, because both come from the same `.map`. On an ungraded form every `isCorrect` is
`false`, every cell is `BAR_COLOR`, and the chart looks exactly as it did in phase 20.

---

## Step 2 — `QuestionResultCard.tsx` — the correct-count line

Replace the answer-count paragraph with a line that carries both figures:

```tsx
    const { answerCount, correctAnswerCount } = question;

    // Omitted on an ungraded form (null) and on a question nobody answered, where it would
    // read "0/0 correct".
    const showCorrectCount = correctAnswerCount !== null && answerCount > 0;
```

```tsx
            <p className="text-xs text-gray-500">
                {answerCount === 0
                    ? 'No answers yet'
                    : `${answerCount} answer${answerCount !== 1 ? 's' : ''}`}

                {showCorrectCount && (
                    <span className="ml-2 text-gray-400">
                        · {correctAnswerCount}/{answerCount} correct
                    </span>
                )}
            </p>
```

The `answerCount > 0` guard is the same rule phases 19–21 follow: a zero divisor never reaches the
screen. `correctAnswerCount !== null` is what keeps an ungraded form unchanged — do not write
`correctAnswerCount &&`, which would also hide a legitimate `0`.

---

## Step 3 — `ValueAnswerList.tsx` — green rows

A Text or Numeric answer matching the question's suggested answer gets the same treatment as a correct
option: green, plus a tick so colour is not the only signal.

**3a.** Add the correctness class to the row:

```tsx
            {values.map(value => {
                const isCorrect = value.isCorrect === true;

                return (
                    <li
                        key={value.value}
                        className={
                            'flex items-center justify-between gap-3 px-3 py-2 ' +
                            (isCorrect ? 'bg-green-50' : '')
                        }
                    >
                        <span className="min-w-0 break-words text-sm text-gray-700">
                            {value.value}
                            {isCorrect && (
                                <span className="ml-1.5 text-green-700" title="Matches the suggested answer">
                                    ✓
                                </span>
                            )}
                        </span>
                        <span
                            className="shrink-0 text-xs tabular-nums text-gray-400"
                            title={`${value.count} respondent${value.count !== 1 ? 's' : ''} gave this answer`}
                        >
                            ({value.count})
                        </span>
                    </li>
                );
            })}
```

`bg-green-50` is the same fill the editor already uses to mark a correct option row
(`OptionRow.tsx`), so the two screens agree on what green means.

Unlike a Multiple question, a Text question **cannot** show a green row with a correct count of zero:
correctness for Text and Numeric is a single comparison, so a green row of count 3 means exactly three
correct answers. Phase 18 computes both from `SubmissionScorer.IsAnswerCorrect`, which is why they
cannot disagree.

---

## Step 4 — Docs

`docs/known-gaps.md` has a row under "Wrong or incomplete on purpose":

> **Nothing warns about a graded form with no answer key** — ... such a question can never be earned,
> scores 0 for everyone, and no message anywhere says so.

That last clause is no longer quite true: the card now reads `0/N correct`, which is a signal, though
not a warning. Amend the row's final sentence to:

```markdown
But such a question can never be earned and scores 0 for everyone. The Results tab now shows it
as `0/N correct` with no green bar, which is the only place the owner can notice — nothing warns
them at the point of saving.
```

Leave everything else in the file for phase 23, which is where the score rows actually stop being
true.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build
```

### 2. Ungraded forms must be unchanged

Before looking at anything graded, open an **ungraded** form's Results tab:

- Every bar is still blue, no ticks, no `x/y correct` anywhere, no green rows.
- If a green bar appears here, something is testing `option.isCorrect` truthily instead of
  `=== true`, or the backend is sending an answer key on an ungraded form.

### 3. A graded Single question

Set up a graded form with a Single question, mark one option correct, and submit a few answers.

- The correct option's bar is **green** and its label ends with `✓`. Every other bar is blue.
- The card reads `4 answers · 3/4 correct`.
- The percentages still use the answer count, so they sum to 100% on a Single question.

### 4. A graded Multiple question — the counter-intuitive one

Mark **two** options correct. Have one respondent pick both, and another pick only one.

- Both key options are green.
- The card reads `2 answers · 1/2 correct`. The respondent who picked only one correct option counts
  as **wrong** — all or nothing.
- Now make everyone pick just one of the two → `0/2 correct` **with two green bars on screen.** This
  is the case from the Context section. It is correct.

### 5. A graded question with no answer key

Leave every option unmarked on a graded question.

- No green bar anywhere.
- The card reads `0/N correct`. Nobody can ever earn this question — `known-gaps.md` records that this
  is allowed on purpose and unwarned.

### 6. Text and Numeric

- Set a suggested answer of `Paris`. Submit `paris`, `PARIS` and `lisbon`.
  → one green row `paris ✓ (2)` and one plain row `lisbon (1)`; the card reads `3 answers · 2/3 correct`.
  The row count and the correct count agree because both use the same comparison.
- Numeric with a suggested answer of `5`: submitting `5` and `5.0` gives one green row with `(2)`.

### 7. The renamed-option case

On a graded form with submissions, rename the option that is marked correct, save, and return to
Results.

- The new option name is green with a count of 0.
- The old recorded text is an ordinary blue bar carrying all the answers.
- The correct count drops to `0/N`.

Uncomfortable, and correct — the stored scores changed the same way when you saved. Do not "fix" it
here.

### 8. Regression

```bash
dotnet test FormAI.sln
```

Unaffected — no backend file changes.
