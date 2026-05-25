import { useState } from "react";
import type { FormQuestion, FormOption, QuestionType } from '../../types/form';
import { Input } from "../Input";
import { Button } from "../Button";

interface AddQuestionModalProps {
  onAdd: (question: FormQuestion) => void;
  onClose: () => void
}

const TYPES: { value: QuestionType, label: string }[] = [
  { value: 'Single', label: 'Single choice' },
  { value: 'Multiple', label: 'Multiple choice' },
  { value: 'Text', label: 'Text' },
  { value: 'Numeric', label: 'Numeric' }
]

function makeOption(): FormOption {
  return { id: crypto.randomUUID(), text: '', order: 0, isCorrect: false };
}

export function AddQuestionModal({ onAdd, onClose }: AddQuestionModalProps) {
  const [text, setText] = useState('');
  const [type, setType] = useState<QuestionType>('Single');
  const [options, setOptions] = useState<FormOption[]>([makeOption(), makeOption()]);
  const [error, setError] = useState('');

  const hasOptions = type === 'Single' || type === 'Multiple';

  function updateOptionText(index: number, value: string) {
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, text: value } : o));
  }

  function toggleCorrect(index: number) {
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, isCorrect: !o.isCorrect } : o));
  }

  function addOption() {
    setOptions(opts => [...opts, makeOption()]);
  }

  function removeOption(index: number) {
    setOptions((opts) => opts.filter((o, i) => i !== index));
  }

  function handleSubmit() {
    if (!text.trim()) {
      setError('Question text is required.');
      return;
    }

    if (hasOptions && options.length < 2) {
      setError('At least two options are required.');
      return;
    }

    if (hasOptions && options.some(o => !o.text.trim())) {
      setError('All option texts must be filled in.');
      return;
    }

    const newQuestion: FormQuestion = {
      id: crypto.randomUUID(),
      text: text.trim(),
      type,
      order: 0,
      isRequired: true,
      aiGenerated: false,
      points: null,
      correctAnswer: null,
      options: hasOptions ? options.map((o, i) => ({ ...o, order: i + 1 })) : []
    };

    onAdd(newQuestion);
    onClose();
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4">
      <div className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-xl">
        <h2 className="mb-4 text-lg font-semibold text-gray-900">Add question</h2>

        <div className="flex flex-col gap-4">
          {/* Label */}
          <Input
            label="Question text"
            value={text}
            onChange={e => setText(e.target.value)}
            placeholder="e.g. What is the capital of France?"
          />

          {/* Type selector */}
          <div>
            <label className="mb-1 block text-sm font-medium text-gray-700">Type</label>
            <div className="flex flex-wrap gap-2">
              {TYPES.map(t => (
                <button
                  key={t.value}
                  onClick={() => setType(t.value)}
                  className={`rounded-full border px-3 py-1 text-sm font-medium transition-colors ${type === t.value
                    ? 'border-indigo-600 bg-indigo-600 text-white'
                    : 'border-gray-300 bg-white text-gray-600 hover:border-indigo-400'
                    }`}
                >
                  {t.label}
                </button>
              ))}
            </div>
          </div>

          {/* Options (only for Single / Multiple) */}
          {hasOptions && (
            <div>
              <label className="mb-2 block text-sm font-medium text-gray-700">
                Options <span className="text-gray-400 font-normal">(check correct answers)</span>
              </label>
              <div className="space-y-2">
                {options.map((opt, i) => (
                  <div key={opt.id} className="flex items-center gap-2">
                    <input
                      type="checkbox"
                      checked={opt.isCorrect}
                      onChange={() => toggleCorrect(i)}
                      className="h-4 w-4 rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                      title="Mark as correct"
                    />
                    <input
                      value={opt.text}
                      onChange={e => updateOptionText(i, e.target.value)}
                      placeholder={`Option ${i + 1}`}
                      className="flex-1 rounded border border-gray-300 px-3 py-1.5 text-sm
                                 focus:outline-none focus:ring-2 focus:ring-indigo-500"
                    />
                    <button
                      onClick={() => removeOption(i)}
                      disabled={options.length <= 2}
                      className="text-gray-400 hover:text-red-500 disabled:opacity-30 disabled:cursor-not-allowed"
                    >
                      ✕
                    </button>
                  </div>
                ))}
                <button
                  onClick={addOption}
                  className="text-xs text-indigo-600 hover:underline"
                >
                  + Add option
                </button>
              </div>
            </div>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        <div className="mt-6 flex justify-end gap-3">
          <Button variant="outline" onClick={onClose}>Cancel</Button>
          <Button onClick={handleSubmit}>Add question</Button>
        </div>
      </div>
    </div>
  );
}