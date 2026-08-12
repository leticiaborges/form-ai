# Phase 5 — Type-Specific Question Editor Components

## Context

The form editor currently uses a single generic `OptionRow` component for both Single and Multiple choice questions, and renders nothing for Text/Numeric questions. Phase 5 replaces the generic approach with four type-specific components and updates the shared option row to match the new UX: trash icon, "Mark correct" button that turns green, row highlight when correct, and AI-suggested answer fields for open-ended questions.

---

## Implementation Order

| Step | File | Action |
|---|---|---|
| 1 | `OptionRow.tsx` | Update |
| 2 | `CheckboxList.tsx` | Create |
| 3 | `RadioButtonList.tsx` | Create |
| 4 | `TextInput.tsx` | Create |
| 5 | `NumberInput.tsx` | Create |
| 6 | `QuestionCard.tsx` | Update |
| 7 | `AddQuestionModal.tsx` | Update |

---

## Step 1 — Update `OptionRow.tsx`

Full file replacement:

```tsx
import { useState } from "react";

interface OptionRowProps {
  text: string;
  isCorrect: boolean;
  inputType: 'checkbox' | 'radio';
  questionId: string;
  onTextChange: (text: string) => void;
  onCorrectChange: (isCorrect: boolean) => void;
  onRemove: () => void;
}

export function OptionRow({
  text,
  isCorrect,
  inputType,
  questionId,
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
    <div className={`flex items-center gap-2 py-1 px-2 rounded-md transition-colors ${isCorrect ? 'bg-green-50' : ''}`}>

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
          className="flex-1 rounded border border-brand-400 px-2 py-0.5 text-sm
                     focus:outline-none focus:ring-1 focus:ring-brand-500"
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
        onClick={() => onCorrectChange(!isCorrect)}
        className={`border rounded px-2 py-0.5 text-xs transition-colors whitespace-nowrap ${
          isCorrect
            ? 'bg-green-100 text-green-700 border-green-300'
            : 'bg-white text-gray-500 border-gray-300 hover:border-gray-400'
        }`}
        title="Mark as correct answer"
      >
        {isCorrect ? 'Correct' : 'Mark correct'}
      </button>

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
}
```

---

## Step 2 — Create `CheckboxList.tsx`

New file at `frontend/src/components/editors/CheckboxList.tsx`:

```tsx
import type { FormOption } from '../../types/form';
import { OptionRow } from './OptionRow';

interface CheckboxListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function CheckboxList({ options, questionId, onOptionsChange }: CheckboxListProps) {

  function updateOption(index: number, patch: Partial<FormOption>) {
    onOptionsChange(options.map((o, i) => i === index ? { ...o, ...patch } : o));
  }

  function removeOption(index: number) {
    onOptionsChange(options.filter((_, i) => i !== index));
  }

  function addOption() {
    onOptionsChange([
      ...options,
      { id: crypto.randomUUID(), text: 'New option', order: options.length + 1, isCorrect: false }
    ]);
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
      <button
        onClick={addOption}
        className="mt-1 text-xs text-brand-600 hover:text-brand-800 hover:underline"
      >
        + Add option
      </button>
    </div>
  );
}
```

---

## Step 3 — Create `RadioButtonList.tsx`

New file at `frontend/src/components/editors/RadioButtonList.tsx`:

```tsx
import type { FormOption } from '../../types/form';
import { OptionRow } from './OptionRow';

interface RadioButtonListProps {
  options: FormOption[];
  questionId: string;
  onOptionsChange: (options: FormOption[]) => void;
}

export function RadioButtonList({ options, questionId, onOptionsChange }: RadioButtonListProps) {

  function markCorrect(targetIndex: number) {
    onOptionsChange(options.map((o, i) => ({ ...o, isCorrect: i === targetIndex })));
  }

  function updateOption(index: number, patch: Partial<FormOption>) {
    onOptionsChange(options.map((o, i) => i === index ? { ...o, ...patch } : o));
  }

  function removeOption(index: number) {
    onOptionsChange(options.filter((_, i) => i !== index));
  }

  function addOption() {
    onOptionsChange([
      ...options,
      { id: crypto.randomUUID(), text: 'New option', order: options.length + 1, isCorrect: false }
    ]);
  }

  return (
    <div className="mt-3 space-y-0.5">
      {options.map((opt, i) => (
        <OptionRow
          key={opt.id}
          text={opt.text}
          isCorrect={opt.isCorrect}
          inputType="radio"
          questionId={questionId}
          onTextChange={(text) => updateOption(i, { text })}
          onCorrectChange={() => markCorrect(i)}
          onRemove={() => removeOption(i)}
        />
      ))}
      <button
        onClick={addOption}
        className="mt-1 text-xs text-brand-600 hover:text-brand-800 hover:underline"
      >
        + Add option
      </button>
    </div>
  );
}
```

---

## Step 4 — Create `TextInput.tsx`

New file at `frontend/src/components/editors/TextInput.tsx`:

```tsx
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
```

---

## Step 5 — Create `NumberInput.tsx`

New file at `frontend/src/components/editors/NumberInput.tsx`:

```tsx
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
          AI suggested answer
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
```

---

## Step 6 — Update `QuestionCard.tsx`

Full file replacement:

```tsx
import { useState } from "react";
import type { FormQuestion } from '../../types/form';
import { CheckboxList } from './CheckboxList';
import { RadioButtonList } from './RadioButtonList';
import { TextInput } from './TextInput';
import { NumberInput } from './NumberInput';

type QuestionType = FormQuestion['type'];

const TYPE_LABEL: Record<QuestionType, string> = {
  Single: 'Single choice',
  Multiple: 'Multiple choice',
  Text: 'Open text',
  Numeric: 'Numeric'
};

const TYPE_COLOR: Record<QuestionType, string> = {
  Single: 'bg-brand-100 text-brand-700',
  Multiple: 'bg-brand-100 text-brand-700',
  Text: 'bg-gray-100 text-gray-600',
  Numeric: 'bg-amber-100 text-amber-700',
};

interface QuestionCardProps {
  question: FormQuestion;
  index: number;
  total: number;
  onChange: (q: FormQuestion) => void;
  onRemove: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

export function QuestionCard({
  question,
  index,
  total,
  onChange,
  onRemove,
  onMoveUp,
  onMoveDown
}: QuestionCardProps) {
  const [isEditingLabel, setIsEditingLabel] = useState(false);
  const [labelDraft, setLabelDraft] = useState(question.text);

  const hasOptions = question.type == 'Single' || question.type == 'Multiple';

  function commitLabel() {
    const trimmed = labelDraft.trim();
    if (trimmed)
      onChange({ ...question, text: trimmed });
    else
      setLabelDraft(question.text);
    setIsEditingLabel(false);
  }

  function renderAnswerEditor() {
    switch (question.type) {
      case 'Single':
        return (
          <RadioButtonList
            options={question.options}
            questionId={question.id}
            onOptionsChange={(opts) => onChange({ ...question, options: opts })}
          />
        );
      case 'Multiple':
        return (
          <CheckboxList
            options={question.options}
            questionId={question.id}
            onOptionsChange={(opts) => onChange({ ...question, options: opts })}
          />
        );
      case 'Text':
        return (
          <TextInput
            correctAnswer={question.correctAnswer}
            onCorrectAnswerChange={(val) => onChange({ ...question, correctAnswer: val })}
          />
        );
      case 'Numeric':
        return (
          <NumberInput
            correctAnswer={question.correctAnswer}
            onCorrectAnswerChange={(val) => onChange({ ...question, correctAnswer: val })}
          />
        );
    }
  }

  return (
    <div className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
      <div className="flex items-start gap-3">
        {/* Question number */}
        <span className="mt-1 min-w-[24px] text-sm font-semibold text-gray-400">
          {index + 1}.
        </span>

        <div className="flex-1 min-w-0">
          {/* Inline label edit */}
          {isEditingLabel ? (
            <textarea
              autoFocus
              value={labelDraft}
              onChange={e => setLabelDraft(e.target.value)}
              onBlur={commitLabel}
              onKeyDown={e => {
                if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); commitLabel(); }
              }}
              rows={2}
              className="w-full resize-none rounded border border-brand-400 px-2 py-1 text-sm
                         font-medium text-gray-900 focus:outline-none focus:ring-1 focus:ring-brand-500"
            />
          ) : (
            <p
              className="cursor-text text-sm font-medium text-gray-900 hover:text-brand-700"
              onClick={() => { setLabelDraft(question.text); setIsEditingLabel(true); }}
            >
              {question.text}
            </p>
          )}

          {/* Type badge */}
          <span className={`mt-1.5 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${TYPE_COLOR[question.type]}`}>
            {TYPE_LABEL[question.type]}
          </span>

          {/* Type-specific answer editor */}
          {renderAnswerEditor()}
        </div>

        {/* Reorder + delete column */}
        <div className="flex flex-col items-center gap-1 shrink-0">
          <button
            disabled={index === 0}
            onClick={onMoveUp}
            title="Move up"
            className="rounded p-1 text-gray-400 hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
          >
            ▲
          </button>
          <button
            disabled={index === total - 1}
            onClick={onMoveDown}
            title="Move down"
            className="rounded p-1 text-gray-400 hover:text-gray-700 disabled:cursor-not-allowed disabled:opacity-30"
          >
            ▼
          </button>
          <button
            onClick={onRemove}
            title="Delete question"
            className="rounded p-1 text-red-400 hover:text-red-600"
          >
            🗑
          </button>
        </div>
      </div>
    </div>
  );
}
```

---

## Step 7 — Update `AddQuestionModal.tsx`

Full file replacement:

```tsx
import { useState } from "react";
import type { FormQuestion, FormOption, QuestionType } from '../../types/form';
import { Input } from "../Input";
import { Button } from "../Button";
import { CheckboxList } from './CheckboxList';
import { RadioButtonList } from './RadioButtonList';

interface AddQuestionModalProps {
  onAdd: (question: FormQuestion) => void;
  onClose: () => void;
}

const TYPES: { value: QuestionType, label: string }[] = [
  { value: 'Single', label: 'Single choice' },
  { value: 'Multiple', label: 'Multiple choice' },
  { value: 'Text', label: 'Text' },
  { value: 'Numeric', label: 'Numeric' }
];

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
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, text: value } : o ));
  }

  function toggleCorrect(){
    setOptions(opts => opts.map((o, i) => i === index ? { ...o, text: value } : o ));
  }

  function addOption(){
    setOptions(opts => [ ...opts, makeOption() ]);
  }

  function removeOption(index: number){
    setOptions(opts => opts.filter((o, i) => i !== index));
  }

  function handleSubmit(){
    if (!text.trim()){
      setError('Question text is required.');
      return;
    }

    if (!hasOptions && options.length < 2){
      setError('At least two options are required');
      return;
    }

    if (hasOptions && options.some(o => !o.text.trim())){
      setError('All options text must be filled in');
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
                  className={`rounded-full border px-3 py-1 text-sm font-medium transition-colors ${
                    type === t.value
                      ? 'border-brand-600 bg-brand-600 text-white'
                      : 'border-gray-300 bg-white text-gray-600 hover:border-brand-400'
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
                Options <span className="text-gray-400 font-normal">(mark correct answers)</span>
              </label>
              {type === 'Single' ? (
                <RadioButtonList
                  options={options}
                  questionId="modal-new-question"
                  onOptionsChange={setOptions}
                />
              ) : (
                <CheckboxList
                  options={options}
                  questionId="modal-new-question"
                  onOptionsChange={setOptions}
                />
              )}
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
```

---

## Key Gotchas

- `OptionRow` is only imported by `QuestionCard` and `AddQuestionModal` — both are replaced here, so removing `showCorrect`/`isMultiple` is safe.
- `correctAnswer` is `string | null` in `FormQuestion`, not a number — `<input type="number">` value is already a string, no parsing needed.
- The initial options in `AddQuestionModal` start as `text: ''`; the list components' add button uses `'New option'`. This is intentional — the modal expects the user to type, while the editor provides a placeholder to click-edit.
- The `AddQuestionModal` `makeOption` helper and `options` state are kept; only the mutation helpers and inline JSX are removed.

---

## Verification

1. Start the frontend dev server (`npm run dev` in `frontend/`).
2. Log in, open an existing form in the editor.
3. **Multiple question:** checkboxes appear; multiple options can be marked green/correct; trash icons work; clicking a label enters edit mode and saves on blur.
4. **Single question:** radio buttons appear; marking one correct automatically unmarks all others.
5. **Text question:** disabled preview textarea visible; "AI suggested answer" input is editable and persists on save.
6. **Numeric question:** same as Text but with number inputs.
7. Open "Add question" modal — confirm the updated option list UX works for both Single and Multiple.
8. Save the form and reload to confirm changes persisted via the API.
