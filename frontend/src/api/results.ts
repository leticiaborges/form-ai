import type { FormResults } from '../types/results';
import api from './axios';

export async function getFormResults(formId: string): Promise<FormResults> {
    const response = await api.get<FormResults>(`/forms/${formId}/results`);
    return response.data;
}