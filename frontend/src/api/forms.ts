import api from './axios';
import type { FormDetail, FormQuestion } from '../types/form';

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
            correctAnswers: q.correctAnswer,
            options: q.options.map((o, oi) => ({
                text: o.text,
                order: oi + 1,
                isCorrect: o.isCorrect
            }))
        }))
    };

    await api.put(`/forms/${formId}/questions`, payload);
}