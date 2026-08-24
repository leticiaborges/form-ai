interface NumberInputProps {
  correctAnswer: string | null;
  onCorrectAnswerChange: (value: string | null) => void;
}


export function NumberInput({ correctAnswer, onCorrectAnswerChange }: NumberInputProps) {
  return (
    <div className="mt-3 space-y-3">
      <div>
        <input
          disabled
          type="number"
          placeholder="0"
          className="w-32 rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-400 cursor-not-allowed"
        />
      </div>
      <div>
        <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-400">
          Suggested answer
        </p>
        <input
          type="number"
          value={correctAnswer ?? ''}
          onChange={e => onCorrectAnswerChange(e.target.value || null)}
          placeholder="Enter the expected numeric answer…"
          className="w-40 rounded-lg border border-gray-300 px-3 py-2 text-sm
                     focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
        />
      </div>
    </div>
  );
}