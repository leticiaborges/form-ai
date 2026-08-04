import { useState } from "react";
import type { FormQuestion, FormOption } from '../../types/form';
import { RadioButtonList } from "./RadioButtonList";
import { CheckBoxList } from "./CheckBoxList";
import { TextInput } from "./TextInput";
import { NumberInput } from "./NumberInput";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from '@dnd-kit/utilities';


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
  onChange: (q: FormQuestion) => void;
  onRemove: () => void;
}

export function QuestionCard({
  question,
  index,
  onChange,
  onRemove
}: QuestionCardProps) {

  const { attributes, listeners, setNodeRef,
    transform, transition, isDragging } =
    useSortable({ id: question.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const [isEditingLabel, setIsEditingLabel] = useState(false);
  const [labelDraft, setLabelDraft] = useState(question.text);

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
    <div ref={setNodeRef} style={style} {...attributes}
      className="rounded-xl border border-gray-200 bg-white p-5 shadow-sm">
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
        <div className="flex flex-col items-center gap-2 shrink-0">
          <div
            {...listeners}
            className="cursor-grab active:cursor-grabbing p-1 text-gray-400 hover:text-gray-600"
            title="Drag to reorder"
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 256 256">
              <path d="M108,60A16,16,0,1,1,92,44,16,16,0,0,1,108,60Zm56,0a16,16,0,1,0-16-16A16,16,0,0,0,164,60ZM92,112a16,16,0,1,0,16,16A16,16,0,0,0,92,112Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,112ZM92,180a16,16,0,1,0,16,16A16,16,0,0,0,92,180Zm72,0a16,16,0,1,0,16,16A16,16,0,0,0,164,180Z" />
            </svg>
          </div>
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