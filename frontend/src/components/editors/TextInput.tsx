interface TextInputProps {
  correctAnswer: string | null;
  onCorrectAnswerChange: (value: string | null) => void;
}

export function TextInput({ correctAnswer, onCorrectAnswerChange }: TextInputProps) {
  return (
    <div className="mt-3 space-y-3">
      <div>
        <textarea
          disabled
          rows={3}
          placeholder="Respondent will type their answer here…"
          className="w-full resize-none rounded-lg border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-400 cursor-not-allowed"
        />
      </div>
      <div>
        <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-400">
          AI suggested answer
        </p>
        <input
          type="text"
          value={correctAnswer ?? ''}
          onChange={e => onCorrectAnswerChange(e.target.value || null)}
          placeholder="Enter the expected correct answer…"
          className="w-full rounded-lg border border-gray-300 px-3 py-2 text-sm
                     focus:outline-none focus:ring-2 focus:ring-brand-500 focus:border-brand-500"
        />
      </div>
    </div>
  );
}