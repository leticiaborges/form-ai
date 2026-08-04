import type { FormOption } from '../../types/form';
import { OptionRow } from './OptionRow';

interface CheckBoxListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function CheckBoxList({
  options, questionId, onOptionsChange
}: CheckBoxListProps) {

  function updateOption(index: number,
    patch: Partial<FormOption>) {
    onOptionsChange(options.map((o, i) =>
      i === index ? { ...o, ...patch } : o));
  }

  function removeOption(index: number) {
    onOptionsChange(options.filter((_, i) => i !== index));
  }

  function addOption() {
    onOptionsChange([
      ...options,
      {
        id: crypto.randomUUID(), text: 'New option',
        order: options.length + 1, isCorrect: false
      }
    ])
  }

  return (
    <div className="mt-3 space-y-0.5">
      {options.map((opt, i) => (
        <OptionRow
          key={opt.id}
          text={opt.text}
          isCorrect={opt.isCorrect}
          inputType="checkbox"
          questionId={questionId}
          onTextChange={(text) => updateOption(i, { text })}
          onCorrectChange={(isCorrect) => updateOption(i, { isCorrect })}
          onRemove={() => removeOption(i)}
        />
      ))}

      <button onClick={addOption}
        className="mt-1 text-xs text-indigo-600 hover:text-indigo-800 hover:underline"
      >
        + Add option
      </button>
    </div>
  )
}