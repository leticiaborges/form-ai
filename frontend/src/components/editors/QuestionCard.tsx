import { useState } from "react";
import type { FormQuestion, FormOption } from '../../types/form';
import { RadioButtonList } from "./RadioButtonList";
import { CheckBoxList } from "./CheckBoxList";
import { TextInput } from "./TextInput";
import { NumberInput } from "./NumberInput";


type QuestionType = FormQuestion['type'];

const TYPE_LABEL: Record<QuestionType, string> = {
  Single: 'Single choice',
  Multiple: 'Multiple choice',
  Text: 'Open text',
  Numeric: 'Numeric'
};

const TYPE_COLOR: Record<QuestionType, string> = {
  Single: 'bg-indigo-100 text-indigo-700',
  Multiple: 'bg-purple-100 text-purple-700',
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
            onOptionsChange={(opts) => onChange({
              ...question,
              options: opts
            })}
          />
        );
      case 'Multiple':
        return (<CheckBoxList
          options={question.options}
          questionId={question.id}
          onOptionsChange={(opts) => onChange({
            ...question,
            options: opts
          })}
        />)
      case 'Text':
        return (
          <TextInput
            correctAnswer={question.correctAnswer}
            onCorrectAnswerChange={(value) =>
              onChange({ ...question, correctAnswer: value })}
          />
        );
      case 'Numeric':
        return (
          <NumberInput
            correctAnswer={question.correctAnswer}
            onCorrectAnswerChange={(value) =>
              onChange({ ...question, correctAnswer: value })}
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
              className="w-full resize-none rounded border border-indigo-400 px-2 py-1 text-sm
                         font-medium text-gray-900 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          ) : (
            <p
              className="cursor-text text-sm font-medium text-gray-900 hover:text-indigo-700"
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