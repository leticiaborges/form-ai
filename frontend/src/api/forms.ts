import api from './axios';
import type { FormDetail, FormQuestion, FormSummary } from '../types/form';

export interface GenerateFormPayload {
    sourceText: string;
    questionCount: number;
    difficultyLevel: string;
    includeCorrectAnswers: boolean;
}

export interface GenerateFormResult {
    formId: string;
    title: string;
}

export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
    const response = await api.post<GenerateFormResult>('/forms/generate/text', {
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