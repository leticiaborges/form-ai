import { useParams } from "react-router-dom";
import type { AnswerForm, AnswerPayload, AnswerQuestion } from "../types/submission";
import { useEffect, useState } from "react";
import { getRespondentToken } from "../utils/respondentToken";
import { getFormToAnswer, getMySubmission, submitForm } from "../api/submissions";
import type { CustomResponse } from "../types/CustomResponse";
import { Button } from "../components/Button";
import { BasePage } from "../components/BasePage";
import { QuestionAnswerCard } from "../components/respond/QuestionAnswerCard";

type PageState = 'loading' | 'alreadySubmitted' |
    'expired' | 'ready' | 'submitting' | 'submitted' | 'error';

function isAnswered(question: AnswerQuestion,
    answer?: AnswerPayload): boolean {
    if (!answer) return false;

    switch (question.type) {
        case 'Single':
        case 'Multiple':
            return (answer.selectedOptionIds?.length ?? 0) > 0;
        case 'Text':
            return !!answer.textValue?.trim();
        case 'Numeric':
            return answer.numericValue !== undefined;
    }
}

export function FormAnswerPage() {
    const { id } = useParams<{ id: string }>();
    const [respondentToken] = useState(getRespondentToken());

    const [state, setState] = useState<PageState>('loading');
    const [form, setForm] = useState<AnswerForm | null>(null);

    const [answers, setAnswers] = useState<Record<string, AnswerPayload>>({});
    const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
    const [submitError, setSubmitError] = useState('');

    useEffect(() => {
        if (!id) return;

        getMySubmission(id, respondentToken)
            .then(sub => {
                if (sub.hasSubmitted) {
                    setState('alreadySubmitted');
                    return;
                }

                return getFormToAnswer(id).then(data => {
                    setForm(data);
                    setState(data.isExpired ? 'expired' : 'ready');
                })
            }).catch(() => setState('error'));
    }, [id, respondentToken]);

    function setSingleAnswer(questionId: string,
        optionId: string) {
        setAnswers(a => ({
            ...a, [questionId]: {
                selectedOptionIds: [optionId]
            }
        }));
    }

    function toggleMultipleAnswer(questionId: string,
        optionId: string) {
        setAnswers(a => {
            const current = a[questionId]?.selectedOptionIds ?? [];
            const next = current.includes(optionId) ?
                current.filter(o => o !== optionId) :
                [...current, optionId];

            return { ...a, [questionId]: { selectedOptionIds: next } };
        });
    }

    function setTextAnswer(questionId: string,
        value: string) {

        setAnswers(a => ({ ...a, [questionId]: { textValue: value } }));
    }

    function setNumericAnswer(questionId: string,
        value: string) {
        setAnswers(a => ({
            ...a, [questionId]: {
                numericValue: value === '' ? undefined : Number(value)
            }
        }));
    }

    function getMessageBasedOnState(state: string): string {
        switch (state) {
            case 'alreadySubmitted':
                return "You've already responded to this form. Thanks!";
            case 'expired':
                return "This form is no longer accepting responses.";
            case 'submitted':
                return "Thanks! Your response has been recorded.";
            case 'error':
                return "Form not found or you don't have access.";
            default:
                return "";
        }
    }

    async function handleSubmit() {
        if (!id || !form) return;

        const missing = form.questions.filter(q =>
            q.isRequired && !isAnswered(q, answers[q.id]));

        if (missing.length > 0) {
            setFieldErrors(Object.fromEntries(missing.map(q => [q.id,
                'This question is required.'
            ])));
            return;
        }

        setFieldErrors({});
        setSubmitError('');
        setState('submitting');

        try {
            await submitForm(id, {
                respondentToken,
                answers: form.questions
                    .filter(q => isAnswered(q, answers[q.id]))
                    .map(q => ({
                        questionId: q.id,
                        ...answers[q.id]
                    }))
            });
            setState('submitted');
        }
        catch (err: unknown) {
            const e = err as CustomResponse;
            setSubmitError(e.response?.data?.message
                ?? 'Failed to submit. Please try again.'
            );
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

    let message = '';
    let status = state == 'error' || !form ? 'error' : state;
    if (status == 'alreadySubmitted' ||
        status == 'expired' ||
        status == 'submitted' || status == 'error' || !form) {
        message = getMessageBasedOnState(state);
        return (
            <BasePage>
                <div className="flex-1 flex items-center justify-center">
                    <p className="text-gray-600">{message}</p>
                </div>
            </BasePage>
        );
    }

    return (
        <BasePage>
            <main className="mx-auto max-w-2xl px-4 py-8 flex flex-col gap-4">
                <div>
                    <h3 className="text-xl font-bold text-gray-900">{form.title}</h3>
                    {form.description && <p className="mt-1 text-sm text-gray-500">{form.description}</p>}
                </div>

                {form.questions.map((q, i) => (
                    <QuestionAnswerCard
                        key={q.id}
                        question={q}
                        index={i}
                        answer={answers[q.id]}
                        error={fieldErrors[q.id]}
                        onSingleChange={optionId => setSingleAnswer(q.id, optionId)}
                        onMultipleToggle={optionId => toggleMultipleAnswer(q.id, optionId)}
                        onTextChange={value => setTextAnswer(q.id, value)}
                        onNumericChange={value => setNumericAnswer(q.id, value)}
                    />
                ))}

                {submitError && <p className="text-center text-sm text-red-600">{submitError}</p>}

                <Button onClick={handleSubmit} isLoading={state === 'submitting'}>
                    Submit
                </Button>
            </main>
        </BasePage>
    );
}

