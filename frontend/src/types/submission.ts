import type { QuestionType } from "./form";

export interface AnswerOption {
    id: string;
    text: string;
    order: number;
}

export interface AnswerQuestion {
    id: string;
    text: string;
    type: QuestionType;
    order: number;
    isRequired: boolean;
    options: AnswerOption[]
}

export interface AnswerForm {
    id: string;
    title: string;
    description: string | null;
    isExpired: boolean;
    questions: AnswerQuestion[]
}

export interface MySubmission {
    hasSubmitted: boolean;
    submittedAt: string | null;
}

export interface AnswerPayload {
    selectedOptionIds?: string[];
    textValue?: string;
    numericValue?: number;
}

export interface SubmitFormPayload {
    respondentToken: string;
    answers: (AnswerPayload & { questionId: string })[];
}

export interface SubmitFormResult {
    submissionId: string;
    score: number | null;
}