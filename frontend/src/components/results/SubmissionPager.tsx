import { useState } from "react";
import { Button } from "../Button";

interface SubmissionPagerProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
  disabled?: boolean;
}

export function SubmissionPager({ page, totalPages, onPageChange, disabled }: Readonly<SubmissionPagerProps>) {
  const [inputValue, setInputValue] = useState(String(page));

  // Resync the input from the page prop when navigation happens elsewhere (Prev/Next, parent).
  // Adjusting during render (rather than in an effect) avoids an extra commit-then-effect pass.
  const [prevPage, setPrevPage] = useState(page);
  if (prevPage !== page) {
    setPrevPage(page);
    setInputValue(String(page));
  }

  function commit() {
    const parsed = Number(inputValue);
    const clamped = Number.isFinite(parsed)
      ? Math.min(Math.max(Math.trunc(parsed), 1), Math.max(totalPages, 1))
      : page;

    setInputValue(String(clamped));
    if (clamped !== page) onPageChange(clamped);
  }

  return (
    <div className="flex items-center justify-center gap-3">
      <Button
        variant="outline"
        aria-label="Previous submission"
        disabled={disabled || page <= 1}
        onClick={() => onPageChange(page - 1)}
      >
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
        </svg>
      </Button>

      <div className="flex items-center gap-2 text-sm text-gray-600">
        <input
          type="number"
          min={1}
          max={totalPages}
          value={inputValue}
          disabled={disabled}
          onChange={e => setInputValue(e.target.value)}
          onBlur={commit}
          onKeyDown={e => { if (e.key === 'Enter') commit(); }}
          className="w-16 rounded-lg border border-gray-300 px-2 py-1 text-center text-sm shadow-sm outline-none
                     focus:ring-2 focus:ring-brand-500 focus:border-brand-500 disabled:bg-gray-50"
        />
        <span>of {totalPages}</span>
      </div>

      <Button
        variant="outline"
        aria-label="Next submission"
        disabled={disabled || page >= totalPages}
        onClick={() => onPageChange(page + 1)}
      >
        <svg className="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
        </svg>
      </Button>
    </div>
  );
}
