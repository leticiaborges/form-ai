# Phase 20 — The option answer distribution chart (ungraded)

## Context

Phase 19 renders each Single/Multiple question's options as a temporary `text — count` list. This
phase replaces that list with the real thing: a **horizontal bar chart**, one bar per option, all bars
the same colour, each labelled with its count and its percentage.

This is the phase that introduces a chart library. Nothing else changes.

### Why a library, and which

`package.json` has no chart library today; `SubmissionsPerFormChart.tsx` is 30 lines of hand-rolled
Tailwind divs. **Recharts 3.10.1** is the choice:

- Its peer range includes `react ^19.0.0`, and this project is on React 19.2.
- It renders **SVG**, so the existing `brand-*` colours apply directly. A canvas library (chart.js)
  would mean re-specifying the palette.
- It is composed of JSX components, which is how everything else in `frontend/src/components` is
  written.
- `<Cell>` gives per-bar colour in one line — which is exactly what phase 22 needs for the green
  correct-option bars. **Build this component so phase 22 only has to add `<Cell>` children.**

### The percentage

Denominator is `answerCount` — the answers to *this question*, per phase 18. So on a **Multiple**
question the percentages will sum past 100%, because one respondent picks several options. That is
correct and needs no note on screen.

`answerCount` can be zero. `QuestionResultCard` already guards that case (`No answers yet`, no chart),
but the chart component guards its own divisor anyway, so it can never emit `NaN%`.

**Scope: frontend only.** No backend changes. Text and Numeric questions still render phase 19's
temporary list — phase 21 handles them.

---

## Files

| File | Change |
|---|---|
| `frontend/package.json` | add `recharts` |
| `frontend/src/components/results/OptionBarChart.tsx` | **new** — the horizontal bar chart |
| `frontend/src/components/results/QuestionResultCard.tsx` | options branch now renders the chart |

---

## Step 1 — Install Recharts

```bash
cd frontend
npm install recharts@^3.10.1
```

Recharts declares `react-is` as a peer dependency. npm 11 installs peer dependencies automatically, so
this should be one command. If the install warns about an unmet peer, add it explicitly:

```bash
npm install react-is
```

Confirm the app still builds before writing any chart code:

```bash
npm run build
```

---

## Step 2 — `OptionBarChart.tsx` (new)

Create `frontend/src/components/results/OptionBarChart.tsx`:

```tsx
import {
    Bar, BarChart, LabelList, ResponsiveContainer, Tooltip, XAxis, YAxis
} from 'recharts';
import type { OptionResult } from '../../types/results';

/** Every bar shares one colour: colour is not encoding a second dimension here. */
const BAR_COLOR = '#0091b8';   // brand-600, from index.css

/** Row height per option, plus the chart's own vertical padding. */
const ROW_HEIGHT = 34;
const CHART_PADDING = 16;

interface OptionBarChartProps {
    options: OptionResult[];
    /** Answers to this question — the percentage denominator. */
    answerCount: number;
}

export function OptionBarChart({ options, answerCount }: Readonly<OptionBarChartProps>) {
    const data = options.map(option => {
        const percentage = answerCount === 0
            ? 0
            : Math.round((option.count / answerCount) * 100);

        return {
            text: option.text,
            count: option.count,
            label: `${option.count} (${percentage}%)`
        };
    });

    // Headroom on the right so the label beside the longest bar is not clipped.
    const max = Math.max(1, ...data.map(d => d.count));

    return (
        <ResponsiveContainer width="100%" height={data.length * ROW_HEIGHT + CHART_PADDING}>
            <BarChart
                data={data}
                layout="vertical"
                margin={{ top: 0, right: 56, bottom: 0, left: 0 }}
            >
                <XAxis type="number" domain={[0, max]} hide />
                <YAxis
                    type="category"
                    dataKey="text"
                    width={140}
                    axisLine={false}
                    tickLine={false}
                    tick={{ fontSize: 12, fill: '#6b7280' }}
                    tickFormatter={(text: string) =>
                        text.length > 22 ? `${text.slice(0, 21)}…` : text}
                />
                <Tooltip
                    cursor={{ fill: 'rgba(0, 0, 0, 0.04)' }}
                    formatter={(value: number) => [value, 'answers']}
                />
                <Bar dataKey="count" fill={BAR_COLOR} radius={[0, 4, 4, 0]} barSize={18}>
                    <LabelList
                        dataKey="label"
                        position="right"
                        style={{ fontSize: 12, fill: '#374151' }}
                    />
                </Bar>
            </BarChart>
        </ResponsiveContainer>
    );
}
```

Points worth understanding rather than copying blindly:

- **`layout="vertical"` is what makes the bars horizontal.** In Recharts the layout names the axis the
  categories run along, not the direction of the bars. This is the single most confusing thing about
  the library, and getting it backwards gives you a working chart that is the wrong way round.
- `<XAxis type="number" hide />` and `<YAxis type="category" />` must both be present even though the
  X axis is hidden. Recharts needs both declared to lay out a chart at all; omitting the hidden one
  renders nothing, with no error.
- **`ResponsiveContainer` needs a real height.** It fills its parent's width but cannot infer height
  inside a flex column, so height is computed from the option count. A question with 5 options gets a
  taller chart than one with 2, and every bar keeps the same thickness.
- The **`tickFormatter` truncation** keeps long option text from eating the plot area; the `<Tooltip>`
  is what makes the full text recoverable on hover. If you drop the tooltip, drop the truncation too.
- `LabelList position="right"` puts `3 (50%)` beside each bar, which is why `margin.right` is 56. An
  option with a count of 0 draws no visible bar but still gets its label at the axis — that is the
  intended reading: "nobody picked this".
- `BAR_COLOR` is a hex literal, not a Tailwind class. Recharts writes SVG `fill` attributes, so
  Tailwind class names do not reach it. `#0091b8` is `--color-brand-600` from `index.css`; if the
  brand palette changes, this constant has to change with it.

---

## Step 3 — `QuestionResultCard.tsx`

Replace the temporary options list with the chart. The values branch is untouched — phase 21 gets it.

**3a.** Add the import:

```tsx
import { OptionBarChart } from './OptionBarChart';
```

**3b.** Replace the temporary `<ul>` block from phase 19 with:

```tsx
            {answerCount > 0 && (
                isSelection
                    ? <OptionBarChart options={question.options} answerCount={answerCount} />
                    : (
                        /* Temporary: phase 21 replaces this with the value list. */
                        <ul className="flex flex-col gap-1 text-sm text-gray-600">
                            {question.values.map(value => (
                                <li key={value.value}>{value.value} — {value.count}</li>
                            ))}
                        </ul>
                    )
            )}
```

Nothing else in the file changes.

---

## Verification

### 1. Build

```bash
cd frontend
npm run build
```

### 2. Run and look at a Single question

```bash
dotnet run --project src/FormAI.API
cd frontend && npm run dev
```

Open a form with submissions and go to **Results**.

- Bars run **left to right**, one per option, all the same blue. If they run bottom-to-top, `layout`
  is wrong — see the note in step 2.
- Each bar is labelled `2 (50%)`. The percentages on a **Single** question sum to 100%.
- An option nobody picked shows `0 (0%)` with no visible bar.
- Options appear in the editor's order, not sorted by count.

### 3. A Multiple question

- Percentages **sum past 100%** when respondents picked more than one option. Correct — do not
  "fix" it.

### 4. Edge cases

- **A renamed option** (rename it in the editor, save, come back): both the new option and the old
  recorded text appear as bars, the old one after the live ones. It looks like any other bar, by
  design.
- **A question with many options** (add six): the chart grows taller, bars keep their thickness, and
  the card does not scroll horizontally.
- **A very long option text** (paste 60 characters): the label truncates with an ellipsis and the
  full text appears on hover.
- **A question nobody answered**: still reads `No answers yet` with no chart — the phase 19 guard.
- **A form with no submissions**: still the empty state, no charts at all.

### 5. Text and Numeric questions

Still show phase 19's plain list. Unchanged, and that is expected until phase 21.

### 6. Bundle sanity

`npm run build` prints the bundle size. Recharts is a meaningful addition — note the number, so that
if you later port `SubmissionsPerFormChart` you can tell whether it was worth it.
