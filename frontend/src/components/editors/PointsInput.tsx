import { useEffect, useState } from "react";

/** Mirrors QuestionPointsValidator on the server. */
export const MIN_POINTS = 0;
export const MAX_POINTS = 100;
export const DEFAULT_POINTS = 1;

interface PointsInputProps {
  points: number | null;
  onChange: (points: number) => void;
}

/**
 * What a question is worth. The field holds a string while it is being edited, so the owner can
 * clear it to type a new number without pushing a null or a NaN into the question, and commits on
 * blur or Enter — coercing anything unusable to the default and clamping to the allowed range.
 * The server never has to reject what this produces.
 */
export function PointsInput({ points, onChange }: PointsInputProps) {
  const [draft, setDraft] = useState(String(points ?? DEFAULT_POINTS));

  // Keep up with changes made elsewhere, such as ticking "Graded form".
  useEffect(() => {
    setDraft(String(points ?? DEFAULT_POINTS));
  }, [points]);

  function commit() {
    const parsed = Number.parseInt(draft, 10);
    const next = Number.isNaN(parsed)
      ? DEFAULT_POINTS
      : Math.min(MAX_POINTS, Math.max(MIN_POINTS, parsed));

    setDraft(String(next));
    onChange(next);
  }

  return (
    <label className="flex shrink-0 items-center gap-1.5 text-xs text-gray-500">
      Points{/**/}
      <input
        type="number"
        min={MIN_POINTS}
        max={MAX_POINTS}
        value={draft}
        onChange={e => setDraft(e.target.value)}
        onBlur={commit}
        onKeyDown={e => {
          if (e.key === 'Enter') { e.preventDefault(); e.currentTarget.blur(); }
        }}
        className="w-16 rounded border border-gray-300 px-2 py-0.5 text-xs text-gray-700
                   focus:outline-none focus:ring-1 focus:ring-brand-400 focus:border-brand-400"
      />
    </label>
  );
}
