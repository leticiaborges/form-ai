import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import type { FormDetail, FormQuestion } from "../types/form";
import { getForm, updateForm, updateQuestions, saveFormEditor } from '../api/forms';
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { QuestionCard } from "../components/editors/QuestionCard";
import { AddQuestionModal } from "../components/editors/AddQuestionModal";
import { DndContext, closestCenter } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';
import { getErrorMessage } from "../utils/getErrorMessage";
import { showSuccess, showError } from "../utils/toast";

type PageState = 'loading' | 'ready' | 'saving' | 'error';

export function FormEditorPage() {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();

    const [state, setState] = useState<PageState>('loading');
    const [form, setForm] = useState<FormDetail | null>(null);
    const [questions, setQuestions] = useState<FormQuestion[]>([]);
    const [showAddModal, setShowAddModal] = useState(false);

    const [isPublic, setIsPublic] = useState(false);
    const [title, setTitle] = useState('');
    const [titleError, setTitleError] = useState('');

    const [description, setDescription] = useState('');
    const [descriptionError, setDescriptionError] = useState('');

    function setFormData(data: FormDetail) {
        setForm(data);
        setQuestions(data.questions);
        setTitle(data.title);
        setDescription(data.description ?? '');
        setIsPublic(data.isPublic);
    }

    useEffect(() => {
        if (!id)
            return;

        getForm(id).then(data => {
            setFormData(data);
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

    function appendQuestion(question: FormQuestion) {
        setQuestions(qs => [...qs, question]);
    }

    async function handleCopyLink(url: string) {
        try {
            await navigator.clipboard.writeText(url);
            showSuccess('Link copied to clipboard.');
        } catch {
            showError('Could not copy link. Please copy it manually.');
        }
    }

    async function handleSave() {
        if (!id)
            return;

        const trimmedTitle = title.trim();
        if (!trimmedTitle) {
            setTitleError('Title is required.');
            return;
        }
        if (trimmedTitle.length > 255) {
            setTitleError('Title must be at most 255 characters.');
            return;
        }

        const trimmedDescription = description.trim();
        if (trimmedDescription.length > 1024) {
            setDescriptionError('Description must be at most 1024 characters.');
            return;
        }

        setState('saving');

        try {
            await saveFormEditor(id, {
                title: trimmedTitle,
                description: trimmedDescription,
                isPublic,
                questions
            });

            const refreshedForm = await getForm(id);
            setFormData(refreshedForm);

            showSuccess('Form saved.');
            setState('ready');
        }
        catch (err: unknown) {
            showError(getErrorMessage(err, 'Failed to save. Please try again.'));
            setState('ready');
        }
    }


    if (state === 'loading') {
        return (
            <BasePage>
                <div className="flex-1 flex items-center justify-center">
                    <p className="text-gray-500">Loading form…</p>
                </div>
            </BasePage>
        );
    }

    if (state === 'error' || !form) {
        return (
            <BasePage>
                <div className="flex-1 flex items-center justify-center">
                    <p className="text-red-600">Form not found or you don't have access.</p>
                </div>
            </BasePage>
        );
    }

    return (
        <BasePage>
            {/* Sticky top bar */}
            <header className="sticky top-0 z-10 border-b border-gray-200 bg-white px-6 py-2 shadow-sm flex items-center justify-between">
                <div>
                    <input
                        value={title}
                        maxLength={255}
                        onChange={e => { setTitle(e.target.value); setTitleError(''); }}
                        className={
                            'max-w-xs truncate text-base font-semibold text-gray-900 bg-transparent ' +
                            'border border-transparent rounded px-1 -mx-1 outline-none ' +
                            'hover:border-gray-200 focus:border-brand-400 focus:ring-1 focus:ring-brand-400 ' +
                            (titleError ? 'border-red-400' : '')
                        }
                    />
                    {titleError && <p className="text-xs text-red-500 px-1">{titleError}</p>}
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

                <textarea
                    value={description}
                    maxLength={1024}
                    rows={4}
                    onChange={e => { setDescription(e.target.value); setDescriptionError(''); }}
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
                    <label className="flex items-center gap-1.5 text-xs text-gray-500 cursor-pointer">
                        <input
                            type="checkbox"
                            checked={isPublic}
                            onChange={e => setIsPublic(e.target.checked)}
                            className="h-3.5 w-3.5 rounded border-gray-300 text-brand-600 focus:ring-brand-400"
                        />
                        Public
                    </label>

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
            </main>

            {showAddModal && (
                <AddQuestionModal
                    onAdd={appendQuestion}
                    onClose={() => setShowAddModal(false)}
                />
            )}
        </BasePage>
    );
}