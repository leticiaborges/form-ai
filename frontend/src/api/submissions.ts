import type {
  MySubmission,
  AnswerForm,
  SubmitFormPayload,
  SubmitFormResult,
} from "../types/submission";
import api from "./axios";

export async function getFormToAnswer(formId: string): Promise<AnswerForm> {
  const response = await api.get<AnswerForm>(`/forms/${formId}/answer`);
  return response.data;
}

export async function getMySubmission(
  formId: string,
  respondentToken: string,
): Promise<MySubmission> {
  const response = await api.get<MySubmission>(`/forms/${formId}/my-submission`, {
    params: { respondentToken },
  });

  return response.data;
}

export async function submitForm(
  formId: string,
  payload: SubmitFormPayload,
): Promise<SubmitFormResult> {
  const response = await api.post<SubmitFormResult>(`/forms/${formId}/submit`, payload);

  return response.data;
}
