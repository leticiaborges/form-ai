import api from "./axios";
import type { FormDetail, FormQuestion, FormSummary } from "../types/form";
import type { SubmissionDetail, SubmissionListItem } from "../types/submissionReview";
import type { PagedResult } from "../types/PagedResult";

export interface GenerateFormPayload {
  title?: string;
  description?: string;
  sourceText: string;
  questionCount: number;
  difficultyLevel: string;
  isGraded: boolean;
  expiresAt: Date;
}

export interface GenerateFormResult {
  formId: string;
  title: string;
}

export interface SaveFormEditorPayload {
  title: string;
  description: string;
  isPublic: boolean;
  isGraded: boolean;
  questions: FormQuestion[];
  expiresAt: Date;
}

export async function saveFormEditor(
  formId: string,
  payload: SaveFormEditorPayload,
): Promise<void> {
  await api.put(`/forms/${formId}/editor`, {
    title: payload.title,
    description: payload.description?.trim() || null,
    isPublic: payload.isPublic,
    isGraded: payload.isGraded,
    expiresAt: payload.expiresAt.toISOString(),
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
        isCorrect: o.isCorrect,
      })),
    })),
  });
}

export async function generateForm(payload: GenerateFormPayload): Promise<GenerateFormResult> {
  const response = await api.post<GenerateFormResult>("/forms/generate/text", {
    title: payload.title?.trim() || null,
    description: payload.description?.trim(),
    sourceText: payload.sourceText,
    sourceType: "Text",
    sourceUrl: null,
    questionCount: payload.questionCount,
    allowedTypes: null,
    difficultyLevel: payload.difficultyLevel,
    isGraded: payload.isGraded,
    expiresAt: payload.expiresAt.toISOString(),
  });
  return response.data;
}

export async function listForms(): Promise<FormSummary[]> {
  const response = await api.get<FormSummary[]>("/forms");
  return response.data;
}

export async function getForm(formId: string): Promise<FormDetail> {
  const response = await api.get<FormDetail>(`/forms/${formId}`);
  return response.data;
}

export async function deleteForm(formId: string): Promise<void> {
  await api.delete(`/forms/${formId}`);
}

export async function getSubmissionCount(formId: string): Promise<number> {
  const response = await api.get<{ submissionCount: number }>(`/forms/${formId}/submissions/count`);

  return response.data.submissionCount;
}

export async function getSubmissions(
  formId: string,
  page: number,
  pageSize: number,
): Promise<PagedResult<SubmissionListItem>> {
  const response = await api.get<PagedResult<SubmissionListItem>>(
    `/forms/${formId}/submissions?page=${page}&pageSize=${pageSize}`,
  );
  return response.data;
}

export async function getSubmissionDetail(
  formId: string,
  submissionId: string,
): Promise<SubmissionDetail> {
  const response = await api.get<SubmissionDetail>(`/forms/${formId}/submissions/${submissionId}`);
  return response.data;
}
