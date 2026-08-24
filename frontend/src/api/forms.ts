import api from './axios';
import type { FormDetail, FormQuestion, FormSummary } from '../types/form';

export interface GenerateFormPayload {
    title?: string;
    description?: string;
    sourceText: string;
    questionCount: number;
    difficultyLevel: string;
    includeCorrectAnswers: boolean;
}

export interface GenerateFormResult {
    formId: string;
    title: string;
}

export interface UpdateFormPayload {
    title: string;
    description: string;
    isPublic: boolean;
    expiresAt: string | null;
    showResultsAfterSubmit: boolean;
}

export interface SaveFormEditorPayload {
    title: string;
    description: string;
    isPublic: boolean;
    questions: FormQuestion[];
}

export async function saveFormEditor(formId: string, payload: SaveFormEditorPayload): Promise<void> {
    await api.put(`/forms/${formId}/editor`, {
        title: payload.title,
        description: payload.description?.trim() || null,
        isPublic: payload.isPublic,
        questions: payload.questions.map((q, i) => ({
            id: q.id,
            text: q.text,
            type: q.type,
            order: i + 1,
            isRequired: q.isRequired,
            aiGenerated: q.aiGenerated,
            points: q.points,
            correctAnswer: q.correctAnswer,
            options: q.options.map((o, oi) => ({
                id: o.id,
                text: o.text,
                order: oi + 1,
                isCorrect: o.isCorrect
            }))
        }))
    });
}

export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
    const response = await api.post<GenerateFormResult>('/forms/generate/text', {
        title: payload.title?.trim() || null,
        description: payload.description?.trim(),
        sourceText: payload.sourceText,
        sourceType: 'Text',
        sourceUrl: null,
        questionCount: payload.questionCount,
        allowedTypes: null,
        difficultyLevel: payload.difficultyLevel,
        includeCorrectAnswers: payload.includeCorrectAnswers,
    });
    return response.data;
}

export async function listForms(): Promise<FormSummary[]> {
    const response = await api.get<FormSummary[]>('/forms');
    return response.data;
}

export async function getForm(formId: string): Promise<FormDetail> {
    const response = await api.get<FormDetail>(`/forms/${formId}`);
    return response.data;
}

export async function updateQuestions(formId: string,
    questions: FormQuestion[]): Promise<void> {

    const payload = {
        questions: questions.map((q, i) => ({
            text: q.text,
            type: q.type,
            order: i + 1,
            isRequired: q.isRequired,
            aiGenerated: q.aiGenerated,
            points: q.points,
            correctAnswer: q.correctAnswer,
            options: q.options.map((o, oi) => ({
                text: o.text,
                order: oi + 1,
                isCorrect: o.isCorrect
            }))
        }))
    };

    await api.put(`/forms/${formId}/questions`, payload);
}

export async function updateForm(formId: string, payload: UpdateFormPayload): Promise<void> {
    await api.put(`/forms/${formId}`, payload);
}