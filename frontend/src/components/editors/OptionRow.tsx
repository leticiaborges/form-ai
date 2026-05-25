import { useState } from "react";

interface OptionRowProps {
  text: string;
  isCorrect: boolean;
  showCorrect: boolean;
  onTextChange: (text: string) => void;
  onCorrectChange: (isCorrect: boolean) => void;
  onRemove: () => void
}

export function OptionRow({
  text,
  isCorrect,
  showCorrect,
  onTextChange,
  onCorrectChange,
  onRemove
}: OptionRowProps) {
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
    <div className="flex items-center gap-2 py-1">
      {showCorrect && (
        <input
          type="checkbox"
          checked={isCorrect}
          onChange={e => onCorrectChange(e.target.checked)}
          className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
          title="Mark as correct answer"
        />
      )}

      {editing ? (
        <input
          autoFocus
          value={draft}
          onChange={e => setDraft(e.target.value)}
          onBlur={commitEdit}
          onKeyDown={e => e.key === 'Enter' && commitEdit()}
          className="flex-1 rounded border border-indigo-400 px-2 py-0.5 text-sm
                     focus:outline-none focus:ring-1 focus:ring-indigo-500"
        />
      ) : (
        <span
          className="flex-1 cursor-text text-sm text-gray-700 hover:text-gray-900"
          onClick={() => { setDraft(text); setEditing(true); }}
        >
          {text || <span className="italic text-gray-400">empty option</span>}
        </span>
      )}

      <button
        onClick={onRemove}
        className="text-gray-400 hover:text-red-500 transition-colors"
        title="Remove option"
      >
        ✕
      </button>
    </div>
  );
};
