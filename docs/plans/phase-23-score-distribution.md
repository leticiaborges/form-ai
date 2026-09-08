# Phase 23 — The score distribution

## Context

The last phase. Everything so far has been per question; this one is about the form as a whole. On a
**graded** form the Results tab gains, above the question cards:

- the form's **total possible points**, and
- **Distribution of points** — a vertical bar chart, one bar per score somebody actually earned, with
  the number of submissions on the Y axis.

The question it answers is the one that motivated the whole feature: *what was the top score, and how
many people got it?*

### Why one bar per achieved score, not one per possible score

Points are per question and owner-chosen (0–100 each), so a form's total can be anything. With six
submissions scoring 3, 7, 7, 8, 12, 12 out of 20:

- **One bar per achieved score** — four bars: 3, 7, 8, 12. Never more bars than there are submissions,
  readable at any form size. The cost: the bars are evenly spaced, so the gap between 3 and 7 does not
  look bigger than the gap between 7 and 8.
- **One bar per integer 0–20** — a true axis, and unreadable the moment somebody sets a question to
  50 points.

Phase 18 already made this choice — `BuildScoreDistribution` emits a bucket only for scores that
occur — so the frontend just renders what it is given. The Recharts detail that enforces it is in
step 2: the X axis must stay `type="category"`. Switching it to `type="number"` would space the bars
by value and reintroduce the empty-bar problem the backend deliberately avoided.

### Where the "out of N" comes from

`totalPoints` is the sum of **every** question's points, computed by the backend. Two consequences
worth knowing rather than discovering:

- It includes questions worth 0, which add nothing.
- It includes questions with **no answer key**, which nobody can ever earn. So on a form with an
  unmarked question, the maximum on the chart can never reach the caption's total, and that is not a
  bug — phase 22's `0/N correct` on that question is where the reason shows.

`docs/known-gaps.md` currently says *"a score of 7 is stored without anything recording that it was
out of 8."* This phase is what makes that false.

**Scope: frontend only, plus docs.** No backend changes — phase 18 shipped `totalPoints` and
`scoreDistribution` already.

---

## Files

| File | Change |
|---|---|
| `frontend/src/components/results/chartColors.ts` | **new** — shared chart colours |
| `frontend/src/components/results/OptionBarChart.tsx` | import the colours instead of defining them |
| `frontend/src/components/results/ScoreDistributionChart.tsx` | **new** — the vertical chart |
| `frontend/src/components/results/ResultsTab.tsx` | render it above the question cards |
| `docs/known-gaps.md`, `CLAUDE.md` | the feature is now built |

---

## Step 1 — `chartColors.ts` (new)

`BAR_COLOR` and `CORRECT_COLOR` currently live inside `OptionBarChart.tsx`. The score chart needs the
first one, and two files each holding their own copy of `#0091b8` is how a palette drifts.

Create `frontend/src/components/results/chartColors.ts`:

```ts
/**
 * Recharts writes SVG fill attributes, so Tailwind class names never reach it — these have to be
 * literals. Keep them in step with the brand tokens in index.css.
 */

/** Every bar in a single-series chart. Colour is not encoding a second dimension. */
export const BAR_COLOR = '#0091b8';      // --color-brand-600

/** An option in the answer key, or an answer matching the suggested answer. */
export const CORRECT_COLOR = '#16a34a';  // green-600
```

Then in `OptionBarChart.tsx`, delete both `const` declarations and import them instead:

```tsx
import { BAR_COLOR, CORRECT_COLOR } from './chartColors';
```

Nothing else in that file changes. `npm run build` will tell you if you missed one — `noUnusedLocals`
flags a constant you deleted the use of, and TypeScript flags one you deleted the declaration of.

---

## Step 2 — `ScoreDistributionChart.tsx` (new)

Create `frontend/src/components/results/ScoreDistributionChart.tsx`:

```tsx
import {
    Bar, BarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis
} from 'recharts';
import type { ScoreBucket } from '../../types/results';
import { BAR_COLOR } from './chartColors';

interface ScoreDistributionChartProps {
    buckets: ScoreBucket[];
    /** The sum of every question's points. Null would mean an ungraded form, which never gets here. */
    totalPoints: number | null;
}

export function ScoreDistributionChart({
    buckets, totalPoints
}: Readonly<ScoreDistributionChartProps>) {

    return (
        <section className="bg-white rounded-2xl shadow-md p-6 flex flex-col gap-1">
            <h3 className="text-base font-medium text-gray-900">Distribution of points</h3>

            <p className="mb-3 text-xs text-gray-500">
                {totalPoints === null
                    ? 'Points earned per submission'
                    : `Out of ${totalPoints} point${totalPoints !== 1 ? 's' : ''} available`}
            </p>

            <ResponsiveContainer width="100%" height={220}>
                <BarChart data={buckets} margin={{ top: 20, right: 8, bottom: 4, left: 0 }}>
                    <XAxis
                        dataKey="score"
                        type="category"
                        tickLine={false}
                        axisLine={{ stroke: '#e5e7eb' }}
                        tick={{ fontSize: 12, fill: '#6b7280' }}
                        label={{ value: 'Points', position: 'insideBottom', offset: -4,
                                 style: { fontSize: 11, fill: '#9ca3af' } }}
                    />
                    <YAxis
                        allowDecimals={false}
                        width={32}
                        tickLine={false}
                        axisLine={false}
                        tick={{ fontSize: 12, fill: '#6b7280' }}
                    />
                    <Tooltip
                        cursor={{ fill: 'rgba(0, 0, 0, 0.04)' }}
                        labelFormatter={(score: number) => `${score} points`}
                        formatter={(value: number) => [value, 'submissions']}
                    />
                    <Bar dataKey="submissionCount" fill={BAR_COLOR} radius={[4, 4, 0, 0]} maxBarSize={56}>
                        <LabelList
                            dataKey="submissionCount"
                            position="top"
                            style={{ fontSize: 12, fill: '#374151' }}
                        />
                    </Bar>
                </BarChart>
            </ResponsiveContainer>
        </section>
    );
}
```

Points worth understanding rather than copying:

- **No `layout` prop.** Recharts' default layout gives vertical bars — this is the plain case, and the
  `layout="vertical"` in `OptionBarChart` is the special one. Compare the two files side by side once;
  that contrast is the clearest way to remember which is which.
- **`type="category"` on the X axis is load-bearing.** The scores are numbers, and Recharts would
  happily treat them as a numeric axis if told to. Category keeps one evenly-spaced bar per achieved
  score, which is the decision from the Context section. Leaving it explicit is a note to the next
  reader that it was chosen.
- **`allowDecimals={false}` on the Y axis.** The Y axis counts submissions. Without this, a chart whose
  tallest bar is 1 gets ticks at 0, 0.25, 0.5 — "half a submission".
- `maxBarSize` keeps a two-bar chart from rendering two enormous slabs.
- `buckets` is already ascending by score from the backend. Do not sort here.
- The chart is not rendered at all when there are no buckets — that guard lives in step 3, not here,
  so this component always has something to draw.

---

## Step 3 — `ResultsTab.tsx`

**3a.** Add the import:

```tsx
import { ScoreDistributionChart } from './ScoreDistributionChart';
```

**3b.** Render it inside the ready branch, between the submission count and the question cards:

```tsx
    return (
        <div className="flex flex-col gap-4">
            <p className="text-sm text-gray-500">
                {results.submissionCount} submission{results.submissionCount !== 1 ? 's' : ''}
            </p>

            {results.isGraded && results.scoreDistribution.length > 0 && (
                <ScoreDistributionChart
                    buckets={results.scoreDistribution}
                    totalPoints={results.totalPoints}
                />
            )}

            {results.questions.map(question => (
                <QuestionResultCard key={question.questionId} question={question} />
            ))}
        </div>
    );
```

Both conditions matter. `isGraded` keeps an ungraded form unchanged. `scoreDistribution.length > 0`
covers the narrow case where a graded form has submissions but none of them carries a score — which
should not happen, because toggling `IsGraded` changes the `GradingFingerprint` and triggers a
rescore, but an empty chart is a worse failure than no chart.

The whole tab is already short-circuited by the `submissionCount === 0` empty state from phase 19, so
a form with no submissions shows no score chart either.

---

## Step 4 — Docs

This is the phase that makes the gap rows false, so this is where they change.

**4a. `docs/known-gaps.md`** — replace the **"Showing a score to anyone"** row entirely. What is now
true: the owner sees the distribution and the total; the respondent still sees nothing, and nobody can
see one individual's score.

```markdown
| **Showing a respondent their score** | `POST {id}/submit` returns the total and the Results tab shows the owner the score distribution out of the form's total points, but no screen shows a respondent what they earned, and `ShowResultsAfterSubmit` still gates nothing. |
```

**4b. `docs/known-gaps.md`** — the **"Individual submissions for the owner"** row (renamed in phase 18)
stays exactly as it is. Aggregated results now exist; per-respondent results still do not.

**4c. `CLAUDE.md`** — in "What works end to end today", replace the closing clause
*"the owner sees a submission count on the dashboard"* with:

```markdown
the owner sees a submission count on the dashboard, and opens the form's Results tab to see each
question's answer distribution and, on a graded form, how the scores were spread
```

**4d. `CLAUDE.md`** — the "Known gaps" headline paragraph lists *"aggregated results for the owner"*
among the things that are not built. Remove it from that list, and change the sentence
*"Scores are computed and stored but nothing displays them"* to *"Scores are shown to the owner in
aggregate but never to the respondent who earned them."*

Check `CONTEXT.md` needs nothing: phase 18 already added **Results**, **Answer distribution** and
**Score distribution**, and this phase introduces no new term.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build
```

The `chartColors.ts` extraction in step 1 is the likeliest thing to break the build — a leftover
`const BAR_COLOR` in `OptionBarChart.tsx` trips `noUnusedLocals` if you added the import but did not
delete the declaration.

### 2. Ungraded forms must be unchanged

Open an ungraded form's Results tab first. No chart above the cards, no "Distribution of points", no
"out of N points". If one appears, the `isGraded` guard in step 3 is missing.

### 3. A graded form

Build a graded form worth, say, 20 points and collect several submissions with different scores.

- **Distribution of points** appears above the question cards, with `Out of 20 points available`.
- Bars are **vertical**, one per distinct score, ascending left to right. If they run sideways, you
  copied `layout="vertical"` from `OptionBarChart`.
- The X axis is labelled `Points`; the Y axis counts submissions and its ticks are whole numbers.
- Each bar carries its submission count above it.
- Two people with the same score produce **one** bar of height 2, not two bars.
- Scores nobody earned get **no bar and no gap** — this is the achieved-scores-only decision.

### 4. The rescore path — the one that ties the feature together

Stand on the **Results** tab of a graded form with submissions. In the editor tab, change an option's
answer key, then Save.

- Switching back to Results, the distribution has moved: `GradingFingerprint` differed, every
  submission was rescored inside the same transaction, and phase 19's `reloadKey` refetched.
- Do the same while standing *on* the Results tab (edit the title in the sticky header, Save) → a
  second `GET /results` fires without switching tabs.

### 5. Turning grading off

Untick **Graded form** and save. This is lossy and irreversible by design.

- The score chart disappears, every `x/y correct` line disappears, and every green bar turns blue.
- Nothing warned you first. That is the documented behaviour, not a regression.

### 6. The uncomfortable total

Add a question to the graded form and leave it with no answer key.

- `Out of N points available` grows by that question's points.
- **No submission can reach N.** The card for that question reads `0/N correct` with no green bar,
  which is the only explanation on screen.

### 7. Full regression

```bash
dotnet build FormAI.sln
dotnet test FormAI.sln
cd frontend && npm run build
```

Then walk the whole feature once: dashboard → open a form → edit a question without saving → switch to
Results → switch back and confirm the edit survived → Save → Results reflects it.

---

## After this phase

The feature is done. What is still not built, and is now recorded accurately in
`docs/known-gaps.md`:

- **Individual submissions** — the owner sees aggregates, never what one respondent answered.
- **A respondent seeing their own score**, and `ShowResultsAfterSubmit` still gating nothing.
- **AI result analysis** — `IAnalysisService` remains an interface with no implementation. The
  results endpoint is the obvious thing to feed it.
- **`SubmissionsPerFormChart`** on the dashboard is still hand-rolled while the Results tab uses
  Recharts. Two chart idioms in one codebase; porting it is a small, self-contained phase 24 if the
  bundle cost you noted in phase 20 turns out to be worth it.
