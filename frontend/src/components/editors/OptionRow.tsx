import { useState } from "react";

interface OptionRowProps {
  text: string;
  isCorrect: boolean | null;
  inputType: 'checkbox' | 'radio';
  questionId: string;
  /** Only a graded form has an answer key, so only a graded form shows the marker. */
  isGraded: boolean;
  onTextChange: (text: string) => void;
  onCorrectChange: (isCorrect: boolean) => void;
  onRemove: () => void
}

export function OptionRow({
  text,
  isCorrect,
  inputType,
  questionId,
  isGraded,
  onTextChange,
  onCorrectChange,
  onRemove
}: Readonly<OptionRowProps>) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(text);

  function commitEdit() {
    const trimmed = draft.trim();
    if (trimmed)
      onTextChange(trimmed);
    else
      setDraft(text);

    setEditing(false);
  }

  return (
    <div className={`flex flex-1 min-w-0 items-center gap-2 py-1 px-2 rounded-md transition-colors ${isGraded && isCorrect ? 'bg-green-50' : ''}`}>

      {inputType === 'checkbox' ? (
        <input
          type="checkbox"
          readOnly
          className="h-4 w-4 rounded border-gray-300 text-brand-600 pointer-events-none"
        />
      ) : (
        <input
          type="radio"
          name={questionId}
          readOnly
          className="h-4 w-4 border-gray-300 text-brand-600 pointer-events-none"
        />
      )}

      {editing ? (
        <input
          autoFocus
          value={draft}
          onChange={e => setDraft(e.target.value)}
          onBlur={commitEdit}
          onKeyDown={e => e.key === 'Enter' && commitEdit()}
          className="flex-1 rounded border border-brand-400 px-2 py-0.5 text-option
                     focus:outline-none focus:ring-1 focus:ring-brand-500"
        />
      ) : (
        <span
          className="flex-1 cursor-text text-option text-gray-700 hover:text-gray-900"
          onClick={() => { setDraft(text); setEditing(true); }}
        >
          {text || <span className="italic text-gray-400">empty option</span>}
        </span>
      )}

      {isGraded && (
        <button
          onClick={() => onCorrectChange(!isCorrect)}
          className={`border rounded px-2 py-0.5 text-xs transition-colors whitespace-nowrap ${isCorrect
            ? 'bg-green-100 text-green-700 border-green-300'
            : 'bg-white text-gray-500 border-gray-300 hover:border-gray-400'
            }`}
          title="Mark as correct answer"
        >
          {isCorrect ? 'Correct' : 'Mark correct'}
        </button>
      )}

      <button
        onClick={onRemove}
        className="text-gray-400 hover:text-red-500 transition-colors"
        title="Remove option"
      >
        <svg xmlns="http://www.w3.org/2000/svg" className="h-4 w-4" viewBox="0 0 24 24" fill="none"
          stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="3 6 5 6 21 6" />
          <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6" />
          <path d="M10 11v6M14 11v6" />
          <path d="M9 6V4a1 1 0 011-1h4a1 1 0 011 1v2" />
        </svg>
      </button>
    </div>
  );
};
