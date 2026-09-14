import { useState } from "react";
import type { FormDetail, FormQuestion } from "../types/form";
import { closestCenter, DndContext, type DragEndEvent } from "@dnd-kit/core";
import { arrayMove, SortableContext, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { showError, showSuccess } from "../utils/toast";
import { QuestionCard } from "./editors/QuestionCard";
import { AddQuestionModal } from "./editors/AddQuestionModal";


interface FormEditorTabProps {
  form: FormDetail;
  questions: FormQuestion[];
  onQuestionsChange: (questions: FormQuestion[]) => void;
  description: string;
  onDescriptionChange: (value: string) => void;
  descriptionError: string;
  isPublic: boolean;
  onIsPublicChange: (value: boolean) => void;
  isGraded: boolean;
  onIsGradedChange: (value: boolean) => void;
}

export function FormEditorTab({
  form,
  questions,
  onQuestionsChange,
  description,
  onDescriptionChange,
  descriptionError,
  isPublic,
  onIsPublicChange,
  isGraded,
  onIsGradedChange
}: Readonly<FormEditorTabProps>) {



  const [showAddModal, setShowAddModal] = useState(false);

  function updateQuestion(index: number, updated: FormQuestion) {
    onQuestionsChange(questions.map((q, i) => i === index ? updated : q));
  }

  function removeQuestion(index: number) {
    onQuestionsChange(questions.filter((_, i) => i !== index));
  }

  function appendQuestion(question: FormQuestion) {
    onQuestionsChange([...questions, question]);
  }

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const oldIndex = questions.findIndex(q => q.id === active.id);
    const newIndex = questions.findIndex(q => q.id === over.id);
    onQuestionsChange(arrayMove(questions, oldIndex, newIndex));
  }

  async function handleCopyLink(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      showSuccess('Link copied to clipboard.');
    } catch {
      showError('Could not copy link. Please copy it manually.');
    }
  }

  return (
    <div className="flex flex-col gap-4">
      {questions.length === 0 && (
        <div className="py-16 text-center text-gray-400">
          <p className="text-lg">No questions yet.</p>
          <p className="mt-1 text-sm">Use the button below to add one.</p>
        </div>
      )}

      <textarea
        value={description}
        maxLength={1024}
        rows={4}
        onChange={e => { onDescriptionChange(e.target.value); }}
        placeholder="Add a description…"
        className={
          'block w-full resize-none text-xs text-gray-500 bg-transparent ' +
          'border border-transparent rounded px-1 -mx-1 outline-none ' +
          'hover:border-gray-200 focus:border-brand-400 focus:ring-1 focus:ring-brand-400 ' +
          (descriptionError ? 'border-red-400' : '')
        }
      />
      {descriptionError && <p className="text-xs text-red-500 px-1">{descriptionError}</p>}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-4">
          <label className="flex items-center gap-1.5 text-xs text-gray-500 cursor-pointer">
            <input
              type="checkbox"
              checked={isPublic}
              onChange={e => onIsPublicChange(e.target.checked)}
              className="h-3.5 w-3.5 rounded border-gray-300 text-brand-600 focus:ring-brand-400"
            />{/**/}
            Public
          </label>

          <label className="flex items-center gap-1.5 text-xs text-gray-500 cursor-pointer">
            <input
              type="checkbox"
              checked={isGraded}
              onChange={e => onIsGradedChange(e.target.checked)}
              className="h-3.5 w-3.5 rounded border-gray-300 text-brand-600 focus:ring-brand-400"
            />{/**/}
            Graded form
          </label>
        </div>

        {form.isPublic && (() => {
          const answerUrl = `${window.location.origin}/forms/${form.id}/answer`;
          return (
            <div className="flex items-center gap-1.5 rounded-full border border-gray-200 bg-white px-2.5 py-1 text-xs text-gray-500">
              <svg className="h-3.5 w-3.5 text-gray-400 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 010 5.656l-3 3a4 4 0 01-5.656-5.656l1.5-1.5M10.172 13.828a4 4 0 010-5.656l3-3a4 4 0 015.656 5.656l-1.5 1.5" />
              </svg>
              <span className="max-w-[220px] truncate" title={answerUrl}>{answerUrl}</span>
              <button
                type="button"
                onClick={() => handleCopyLink(answerUrl)}
                className="flex items-center gap-1 rounded px-1 text-gray-400 hover:text-brand-600"
                title="Copy link">

                <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M8 16H6a2 2 0 01-2-2V6a2 2 0 012-2h8a2 2 0 012 2v2m-6 12h8a2 2 0 002-2v-8a2 2 0 00-2-2h-8a2 2 0 00-2 2v8a2 2 0 002 2z" />
                </svg>

              </button>
            </div>
          );
        })()}
      </div>

      <DndContext collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
        <SortableContext items={questions.map(q => q.id)} strategy={verticalListSortingStrategy}>
          {questions.map((q, i) => (
            <QuestionCard
              key={q.id}
              question={q}
              index={i}
              isGraded={isGraded}
              onChange={updated => updateQuestion(i, updated)}
              onRemove={() => removeQuestion(i)}
            />
          ))}
        </SortableContext>
      </DndContext>


      {/* Add question trigger */}
      <button
        onClick={() => setShowAddModal(true)}
        className="flex items-center justify-center gap-2 rounded-xl border-2 border-dashed
                     border-gray-300 py-4 text-sm text-gray-500 transition-colors
                     hover:border-brand-400 hover:text-brand-600"
      >
        + Add question
      </button>

      {showAddModal && (
        <AddQuestionModal
          isGraded={isGraded}
          onAdd={appendQuestion}
          onClose={() => setShowAddModal(false)}
        />
      )}

    </div>


  );
}