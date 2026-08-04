import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { FormDetail, FormQuestion } from "../types/form";
import { getForm, updateQuestions } from '../api/forms';
import { Button } from "../components/Button";
import { QuestionCard } from "../components/editors/QuestionCard";
import { AddQuestionModal } from "../components/editors/AddQuestionModal";
import { DndContext, closestCenter } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';

type PageState = 'loading' | 'ready' | 'saving' | 'error';

export function FormEditorPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();


    const [state, setState] = useState<PageState>('loading');
    const [form, setForm] = useState<FormDetail | null>(null);
    const [questions, setQuestions] = useState<FormQuestion[]>([]);
    const [showAddModal, setShowAddModal] = useState(false);
    const [saveError, setSaveError] = useState('');

    useEffect(() => {
        if (!id)
            return;

        getForm(id).then(data => {
            setForm(data);
            setQuestions(data.questions);
            setState('ready');
        }).catch(() => setState('error'));
    }, [id]);

    function updateQuestion(index: number, updated: FormQuestion) {
        setQuestions(qs => qs.map((q, i) => i === index ? updated : q));
    }

    function removeQuestion(index: number) {
        setQuestions(qs => qs.filter((_, i) => i !== index));
    }

    function handleDragEnd(event: DragEndEvent) {
        const { active, over } = event;
        if (!over || active.id === over.id) return;
        setQuestions(qs => {
            const oldIndex = qs.findIndex(q => q.id === active.id);
            const newIndex = qs.findIndex(q => q.id === over.id);
            return arrayMove(qs, oldIndex, newIndex);
        });
    }

    // function moveQuestion(index: number, direction: -1 | 1) {
    //     setQuestions(qs => {
    //         const next = [...qs];
    //         const target = index + direction;
    //         if (target < 0 || target >= next.length)
    //             return qs;

    //         [next[index], next[target]] = [next[target], next[index]];
    //         return next;
    //     });
    // }

    function appendQuestion(question: FormQuestion) {
        setQuestions(qs => [...qs, question]);
    }

    async function handleSave() {
        if (!id)
            return;

        setSaveError('');
        setState('saving');

        try {
            await updateQuestions(id, questions);
            setState('ready');
        }
        catch {
            setSaveError('Failed to save. Please try again');
            setState('ready');
        }
    }


    if (state === 'loading') {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-gray-500">Loading form…</p>
            </div>
        );
    }

    if (state === 'error' || !form) {
        return (
            <div className="min-h-screen flex items-center justify-center">
                <p className="text-red-600">Form not found or you don't have access.</p>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-gray-50">
            {/* Sticky top bar */}
            <header className="sticky top-0 z-10 border-b border-gray-200 bg-white px-6 py-3 shadow-sm flex items-center justify-between">
                <div>
                    <h1 className="max-w-xs truncate text-base font-semibold text-gray-900">
                        {form.title}
                    </h1>
                    <p className="text-xs text-gray-400">
                        {questions.length} question{questions.length !== 1 ? 's' : ''}
                    </p>
                </div>
                <div className="flex items-center gap-3">
                    <Button variant="outline" onClick={() => navigate('/dashboard')}>
                        Back
                    </Button>
                    <Button onClick={handleSave} isLoading={state === 'saving'}>
                        Save
                    </Button>
                </div>
            </header>

            {/* Editor area */}
            <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">
                {questions.length === 0 && (
                    <div className="py-16 text-center text-gray-400">
                        <p className="text-lg">No questions yet.</p>
                        <p className="mt-1 text-sm">Use the button below to add one.</p>
                    </div>
                )}

                <DndContext collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
                    <SortableContext items={questions.map(q => q.id)} strategy={verticalListSortingStrategy}>
                        {questions.map((q, i) => (
                            <QuestionCard
                                key={q.id}
                                question={q}
                                index={i}
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
                     hover:border-indigo-400 hover:text-indigo-600"
                >
                    + Add question
                </button>

                {saveError && (
                    <p className="text-center text-sm text-red-600">{saveError}</p>
                )}
            </main>

            {showAddModal && (
                <AddQuestionModal
                    onAdd={appendQuestion}
                    onClose={() => setShowAddModal(false)}
                />
            )}
        </div>
    );
}