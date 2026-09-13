export interface SubmissionListItem {
    submissionId: string;
    submittedAt: string;
}

export interface SubmissionDetail {
    formId: string;
    submissionId: string;
    submittedAt: string;
    score: number | null;
    answers: SubmissionAnswerDetail[];
}

export interface SubmissionAnswerDetail {
    questionId: string;
    selectedOptions: string[];
    numericValue: number | null;
    textValue: string | null;
    isCorrect: boolean | null;
    score: number | null;
}
