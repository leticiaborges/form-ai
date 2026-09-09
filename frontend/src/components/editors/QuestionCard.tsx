import { useState } from "react";
import type { FormQuestion } from '../../types/form';
import { RadioButtonList } from "./RadioButtonList";
import { CheckBoxList } from "./CheckBoxList";
import { TextInput } from "./TextInput";
import { NumberInput } from "./NumberInput";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from '@dnd-kit/utilities';
import { DragHandleRail } from "./DragHandleRail";
import { PointsInput } from "./PointsInput";


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
  /** Whether the form is graded: the answer key and suggested answer only exist if it is. */
  isGraded: boolean;
  onChange: (q: FormQuestion) => void;
  onRemove: () => void;
}

export function QuestionCard({
  question,
  index,
  isGraded,
  onChange,
  onRemove
}: Readonly<QuestionCardProps>) {

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
            isGraded={isGraded}
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
          isGraded={isGraded}
          onOptionsChange={(opts) => onChange({
            ...question,
            options: opts
          })}
        />)
      case 'Text':
        return (
          <TextInput
            correctAnswer={question.correctAnswer}
            isGraded={isGraded}
            onCorrectAnswerChange={(value) =>
              onChange({ ...question, correctAnswer: value })}
          />
        );
      case 'Numeric':
        return (
          <NumberInput
            correctAnswer={question.correctAnswer}
            isGraded={isGraded}
            onCorrectAnswerChange={(value) =>
              onChange({ ...question, correctAnswer: value })}
          />
        );
    }
  }

  return (
    <div ref={setNodeRef} style={style} {...attributes}
      className="flex items-stretch overflow-hidden rounded-xl border border-gray-200 bg-white shadow-sm">
      <div className="flex-1 min-w-0 p-5">
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
                className="w-full resize-none rounded border border-brand-400 px-2 py-1 text-question
                           font-medium text-gray-900 focus:outline-none focus:ring-1 focus:ring-brand-500"
              />
            ) : (
              <p
                className="cursor-text text-question font-medium text-gray-900 hover:text-brand-700"
                onClick={() => { setLabelDraft(question.text); setIsEditingLabel(true); }}
              >
                {question.text}
              </p>
            )}

            {/* Type badge on the left, what the question is worth on the right */}
            <div className="mt-1.5 flex items-center justify-between gap-3">
              <span className={`inline-block rounded-full px-2 py-0.5 text-xs font-medium ${TYPE_COLOR[question.type]}`}>
                {TYPE_LABEL[question.type]}
              </span>

              {isGraded && (
                <PointsInput
                  points={question.points}
                  onChange={points => onChange({ ...question, points })}
                />
              )}
            </div>

            {/* Type-specific answer editor */}
            {renderAnswerEditor()}
          </div>

          <button
            onClick={onRemove}
            title="Delete question"
            className="shrink-0 rounded p-1 text-red-400 hover:text-red-600"
          >
            🗑
          </button>
        </div>
      </div>

      {/* Drag handle rail, attached to the card via a divider */}
      <DragHandleRail listeners={listeners} />
    </div>
  );
}